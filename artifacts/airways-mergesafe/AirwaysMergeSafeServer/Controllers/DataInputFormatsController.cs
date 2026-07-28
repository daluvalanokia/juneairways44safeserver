using AirwaysMergeSafeServer.Data;
using AirwaysMergeSafeServer.Filters;
using AirwaysMergeSafeServer.Models;
using AirwaysMergeSafeServer.Services;
using AirwaysMergeSafeServer.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Nodes;
using AirwaysMergeSafeServer.Infrastructure;

namespace AirwaysMergeSafeServer.Controllers;

/// <summary>
/// Phase 6: SimulationPost now calls VehicleClassifier.Classify() on every
/// generated payload and stores the result in the VehicleEvent record.
/// The classification result is also returned in the JSON response so the
/// UI can immediately show the classified vehicle type without a page reload.
/// </summary>
public class DataInputFormatsController : Controller
{
    private readonly AppDbContext        _db;
    private readonly InputPayloadService _payloadSvc;
    private readonly VehicleClassifier   _classifier;
    private readonly ILogger<DataInputFormatsController> _logger;

    public DataInputFormatsController(
        AppDbContext        db,
        InputPayloadService payloadSvc,
        VehicleClassifier   classifier,
        ILogger<DataInputFormatsController> logger)
    { _db = db; _payloadSvc = payloadSvc; _classifier = classifier; _logger = logger; }

    private bool IsAjax => Request.Headers["X-Requested-With"] == "XMLHttpRequest";

    public async Task<IActionResult> Index(string activeTab = "physical")
    {
        TraceLogger.Enter("DataInputFormats", nameof(Index));
        var highways   = await _db.Highways.AsNoTracking().Where(h => h.IsActive).OrderBy(h => h.Name).ToListAsync();
        var highwayId  = HttpContext.Session.GetString("HighwayId");
        var allConfigs = await _db.InputFormatConfigs.AsNoTracking().OrderBy(c => c.FormatName).ToListAsync();
        var payloads   = await _db.SamplePayloads.AsNoTracking().OrderByDescending(p => p.CreatedDate).Take(30).ToListAsync();
        var zones      = await _db.MergeZones.AsNoTracking().OrderBy(z => z.HighwayId).ThenBy(z => z.ZoneName).ToListAsync();
        var zoneIds    = zones.Select(z => z.ZoneId).ToList();
        var srvs       = await _db.SwitchServers.AsNoTracking()
                            .Where(s => s.ZoneId != null && zoneIds.Contains(s.ZoneId))
                            .OrderBy(s => s.ServerName).ToListAsync();

        TraceLogger.Exit("DataInputFormats", nameof(Index));
        return View(new DataInputFormatsViewModel
        {
            Highways          = highways,
            SelectedHighwayId = highwayId,
            ActiveTab         = activeTab,
            PhysicalConfigs   = allConfigs.Where(c => c.SourceType == "physical").ToList(),
            SatelliteConfigs  = allConfigs.Where(c => c.SourceType == "satellite").ToList(),
            TelecomConfigs    = allConfigs.Where(c => c.SourceType == "telecom").ToList(),
            TrackerConfigs    = allConfigs.Where(c => c.SourceType == "tracker").ToList(),
            AirFlyCarConfigs  = allConfigs.Where(c => c.SourceType == "airflycar").ToList(), // Phase 8
            SavedPayloads     = payloads,
            AllZones          = zones,
            AllSwitchServers  = srvs,
        });
    }

    /// <summary>
    /// Phase 6: Classify payload, write classified VehicleEvent, return classification
    /// in JSON response so the UI can render the correct vehicle shape immediately.
    /// </summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SimulationPost(
        string? highwayId, string? zoneId, string? serverId, string? sourceType)
    {
        TraceLogger.Enter("DataInputFormats", nameof(SimulationPost));
        var type          = sourceType ?? "physical";
        var isAirFlyCarSrc = string.Equals(type, "airflycar", StringComparison.OrdinalIgnoreCase);
        // Task 10: all formats carry isAirFlyCar explicitly (Y for airflycar source, N for others)
        var fields = isAirFlyCarSrc
            ? new[] {
                "vehicle_id","timestamp","latitude","longitude","altitude_m","speed_mph","heading",
                "vehicle_type","vehicle_make","flight_phase","vertical_rate_fpm","battery_soc","battery_temp_c",
                "range_remaining_km","rotor_rpm","rotor_health","motor_temp_c","noise_db",
                "corridor_id","corridor_deviation_m","conflict_flag","separation_m",
                "passenger_count","destination_pad","pilot_id","icao_address","squawk",
                "zone_id","highway_id","event_type","isAirFlyCar","nic","nac_p"
              }
            : new[] {
                "vehicle_id","timestamp","speed_mph","latitude","longitude",
                "altitude_m","direction","lane","vehicle_type","vehicle_make","event_type",
                "zone_id","highway_id","signal_strength","isAirFlyCar"
              };

        // Look up the selected zone's GPS + all highway zones to compute
        // the real road bearing from actual zone coordinates.
        double? _zLat = null, _zLon = null;
        double  _hwBearing = 90.0;  // default East

        if (!string.IsNullOrEmpty(highwayId))
        {
            var _hwZones = await _db.MergeZones.AsNoTracking()
                .Where(z => z.HighwayId == highwayId && z.Latitude.HasValue)
                .OrderBy(z => z.Latitude)
                .Select(z => new { z.ZoneId, z.Latitude, z.Longitude })
                .ToListAsync();

            if (_hwZones.Count >= 2)
            {
                // Compute bearing from first zone to last zone (real road heading)
                var _first = _hwZones[0];
                var _last  = _hwZones[^1];
                double _midLat = (_first.Latitude!.Value + _last.Latitude!.Value) / 2.0;
                double _cosLat = Math.Cos(_midLat * Math.PI / 180.0);
                double _dLat = _last.Latitude!.Value - _first.Latitude!.Value;
                double _dLon = _last.Longitude!.Value - _first.Longitude!.Value;
                _hwBearing = Math.Atan2(_dLon * _cosLat, _dLat) * 180.0 / Math.PI;
                if (_hwBearing < 0) _hwBearing += 360;
                TraceLogger.Info("DataInputFormats", nameof(SimulationPost),
                    $"Highway bearing from zones: {_hwBearing:F1}° ({_hwZones.Count} zones)");
            }

            // Also get the selected zone's GPS
            if (!string.IsNullOrEmpty(zoneId))
            {
                var _sel = _hwZones.FirstOrDefault(z => z.ZoneId == zoneId);
                if (_sel?.Latitude.HasValue == true)  _zLat = _sel.Latitude!.Value;
                if (_sel?.Longitude.HasValue == true) _zLon = _sel.Longitude!.Value;
            }
        }
        else if (!string.IsNullOrEmpty(zoneId))
        {
            var _zone = await _db.MergeZones.AsNoTracking()
                .Where(z => z.ZoneId == zoneId).FirstOrDefaultAsync();
            if (_zone?.Latitude.HasValue == true)  _zLat = _zone.Latitude!.Value;
            if (_zone?.Longitude.HasValue == true) _zLon = _zone.Longitude!.Value;
        }

        var payload = _payloadSvc.Generate(
            type, fields,
            customFields:  null,
            zoneLat:       _zLat,
            zoneLon:       _zLon,
            zoneRadiusDeg: 0.012,    // ~1.3 km — tight corridor spread on highway
            zoneId:        zoneId,
            highwayId:     highwayId,
            highwayBearing: _hwBearing);
        // Task 10: for non-airflycar sources, enforce isAirFlyCar="N" in payload BEFORE
        // classification, so the classifier never promotes them to air via the Y-field gate.
        if (!isAirFlyCarSrc)
            payload = ForceIsAirFlyCarN(payload);
        var label   = $"Simulation [{type.ToUpper()}] — {DateTime.UtcNow:HH:mm:ss}";
        var now     = DateTime.UtcNow;

        // ── Phase 6: Classify the payload ─────────────────────────────────
        var vc = _classifier.Classify(payload, type);
        var vcJson = JsonSerializer.Serialize(vc, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        _db.SamplePayloads.Add(new SamplePayload
        {
            SourceType  = type,
            Label       = label,
            Payload     = payload,
            IsValid     = true,
            CreatedDate = now
        });

        // Hoist savedVehicleId above try{} so it is in scope for the return Json() below.
        string savedVehicleId;
        {
            using var _preDoc  = JsonDocument.Parse(payload);
            var _preRoot       = _preDoc.RootElement;
            var _rawVid        = _preRoot.TryGetProperty("vehicle_id", out var _vp)
                                 && _vp.GetString() is { Length: > 0 } _vs ? _vs
                                 : Guid.NewGuid().ToString("N")[..8];
            savedVehicleId = $"SIM-{_rawVid}";
        }

        // Write classified VehicleEvent
        VehicleEvent? _savedEvent = null;
        try
        {
            using var doc  = JsonDocument.Parse(payload);
            var root       = doc.RootElement;
            string GetStr(string k) => root.TryGetProperty(k, out var v) ? (v.GetString() ?? "") : "";
            double? GetDbl(string k) => root.TryGetProperty(k, out var v) &&
                                        v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

            var hw  = !string.IsNullOrEmpty(highwayId) ? highwayId : GetStr("highway_id");
            // Always use the real form zoneId — never the random payload zone_id
            var zid = !string.IsNullOrEmpty(zoneId) ? zoneId : "";
            var et  = GetStr("event_type") is { Length: > 0 } e ? e : "detection";

            // Task 10: determine IsAirFlyCar — forced "Y" for airflycar source, or if payload field set
            var iafRaw = GetStr("isAirFlyCar");
            var isAirFlyCarVal = (string.Equals(type, "airflycar", StringComparison.OrdinalIgnoreCase)
                || string.Equals(iafRaw, "Y", StringComparison.OrdinalIgnoreCase)) ? "Y" : "N";

            _savedEvent = new VehicleEvent
            {
                EventType        = et,
                ZoneId           = zid,
                HighwayId        = hw,
                VehicleId        = savedVehicleId,   // from hoisted block above
                SpeedMph         = GetDbl("speed_mph"),
                Latitude         = GetDbl("latitude"),
                Longitude        = GetDbl("longitude"),
                AltitudeMeters   = vc.AltitudeM,
                // Phase 6 classification fields
                VehicleMode      = vc.Domain,
                VehicleCategory  = vc.Category,
                VehicleClassJson = vcJson[..Math.Min(800, vcJson.Length)],
                // Task 10: explicit air-fly-car flag
                IsAirFlyCar      = isAirFlyCarVal,
                Payload          = payload.Length > 490 ? payload[..490] : payload,
                CreatedDate      = now
            };
            _db.VehicleEvents.Add(_savedEvent);
        }
        catch (Exception _evEx) { _logger.LogWarning("SimPost: VehicleEvent save failed: {Msg}", _evEx.Message); }
        // ── Update SimulationStatus in database ──────────────────────────────
        // Maintain a persistent server-side record so the app knows the sim
        // was recently active even after a restart.  This is the authoritative
        // source — localStorage is only a hint for the browser.
        try
        {
            var simStatus = await _db.SimulationStatuses
                .OrderByDescending(s => s.Id)
                .FirstOrDefaultAsync();

            if (simStatus == null)
            {
                simStatus = new SimulationStatus { Id = 0 };
                _db.SimulationStatuses.Add(simStatus);
            }

            simStatus.IsRunning    = true;
            simStatus.HighwayId    = highwayId;
            simStatus.ZoneId        = zoneId;
            simStatus.ServerId     = serverId;
            simStatus.SourceType    = type;
            simStatus.TotalPosted  = simStatus.TotalPosted + 1;
            simStatus.LastHeartbeat = DateTime.UtcNow;
            simStatus.StoppedAt    = null;
        }
        catch (Exception exSim)
        {
            TraceLogger.Error("DataInputFormats", nameof(SimulationPost), exSim);
            _logger.LogWarning("SimPost: Failed to update SimulationStatus: {Message}", exSim.Message);
        }

        await _db.SaveChangesAsync();

        // Return classification in response so JS can update the scene immediately
        return Json(new {
            ok    = true,
            label,
            payload,
            // vehicleId returned so the 3D scene JS can match meshes by stable id
            vehicleId = savedVehicleId,
            // dbId returned so the JS sim panel can show "saved" status
            dbId = _savedEvent?.Id,
            classification = new {
                domain      = vc.Domain,
                category    = vc.Category,
                make        = vc.Make,
                color       = vc.Color,
                shape       = vc.Shape3D,
                confidence  = vc.Confidence,
                lowConf     = vc.LowConfidence,
                altitudeM   = vc.AltitudeM,
                speedMph    = vc.SpeedMph,
                isAir       = vc.Domain == "air"
            }
        });
    }

    /// <summary>
    /// Server-side simulation stop — called by the client when the simulation
    /// is stopped or the page is unloaded.  Cleans up simulation-generated
    /// SamplePayloads and VehicleEvents so they don't accumulate in the DB.
    /// </summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SimulationStop()
    {
        TraceLogger.Enter("DataInputFormats", nameof(SimulationStop));
        try
        {
            // ── Mark simulation as stopped in the database ────────────────────
            var simStatus = await _db.SimulationStatuses
                .OrderByDescending(s => s.Id)
                .FirstOrDefaultAsync();
            if (simStatus != null)
            {
                simStatus.IsRunning  = false;
                simStatus.StoppedAt  = DateTime.UtcNow;
                simStatus.TotalPosted = 0;
                _db.SimulationStatuses.Update(simStatus);
            }

            // Delete recent simulation-generated payloads (label starts with "Simulation [")
            var simPayloads = _db.SamplePayloads
                .Where(p => p.Label != null && p.Label.StartsWith("Simulation ["));
            _db.SamplePayloads.RemoveRange(simPayloads);

            // Delete simulation-generated VehicleEvents (VehicleId starts with "SIM-")
            var simEvents = _db.VehicleEvents
                .Where(v => v.VehicleId != null && v.VehicleId.StartsWith("SIM-"));
            _db.VehicleEvents.RemoveRange(simEvents);

            await _db.SaveChangesAsync();
            return Json(new { ok = true, message = "Simulation stopped and records cleaned up" });
        }
        catch (Exception ex)
        {
            TraceLogger.Error("DataInputFormats", nameof(SimulationStop), ex);
        TraceLogger.Exit("DataInputFormats", nameof(SimulationStop));
            return Json(new { ok = false, error = ex.Message });
        }
    }

    /// <summary>
    /// GET endpoint — returns the current simulation status from the database.
    /// The client calls this on page load to decide whether to auto-resume.
    /// If the DB says IsRunning=false, the client will NOT resume from localStorage.
    /// </summary>
    [HttpGet, SkipSessionAuth]
    public async Task<IActionResult> SimulationStatus()
    {
        TraceLogger.Enter("DataInputFormats", nameof(SimulationStatus));
        try
        {
            var simStatus = await _db.SimulationStatuses
                .OrderByDescending(s => s.Id)
                .FirstOrDefaultAsync();

            if (simStatus == null)
                return Json(new { isRunning = false, stale = false, totalPosted = 0 });

            var heartbeatAge = DateTime.UtcNow - simStatus.LastHeartbeat;
            var isStale      = simStatus.IsRunning && heartbeatAge.TotalMinutes > 2;

            // If stale, auto-mark as stopped
            if (isStale)
            {
                simStatus.IsRunning = false;
                simStatus.StoppedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
            }

            return Json(new
            {
                isRunning   = isStale ? false : simStatus.IsRunning,
                stale       = isStale,
                totalPosted = simStatus.TotalPosted,
                highwayId   = simStatus.HighwayId ?? "",
                zoneId      = simStatus.ZoneId ?? "",
                serverId    = simStatus.ServerId ?? "",
                sourceType   = simStatus.SourceType ?? "",
                heartbeatAgeSec = (int)heartbeatAge.TotalSeconds
            });
        }
        catch (Exception ex)
        {
            TraceLogger.Error("DataInputFormats", nameof(SimulationStatus), ex);
        TraceLogger.Exit("DataInputFormats", nameof(SimulationStatus));
            return Json(new { isRunning = false, stale = false, error = ex.Message });
        }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(InputFormatConfig model, string[] enabledFields, string[]? customFieldNames)
    {
        TraceLogger.Enter("DataInputFormats", nameof(Create));
        var combined = enabledFields.ToList();
        if (customFieldNames != null) combined.AddRange(customFieldNames.Where(n => !string.IsNullOrWhiteSpace(n)));
        model.EnabledFieldsRaw = string.Join(",", combined);
        _db.InputFormatConfigs.Add(model);
        await _db.SaveChangesAsync();
        if (IsAjax) return Json(new { ok = true, activeTab = model.SourceType });
        TraceLogger.Exit("DataInputFormats", nameof(Create));
        return RedirectToAction(nameof(Index), new { activeTab = model.SourceType });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(InputFormatConfig model, string[] enabledFields, string[]? customFieldNames)
    {
        TraceLogger.Enter("DataInputFormats", nameof(Edit));
        var combined = enabledFields.ToList();
        if (customFieldNames != null) combined.AddRange(customFieldNames.Where(n => !string.IsNullOrWhiteSpace(n)));
        model.EnabledFieldsRaw = string.Join(",", combined);
        _db.InputFormatConfigs.Update(model);
        await _db.SaveChangesAsync();
        if (IsAjax) return Json(new { ok = true, activeTab = model.SourceType });
        TraceLogger.Exit("DataInputFormats", nameof(Edit));
        return RedirectToAction(nameof(Index), new { activeTab = model.SourceType });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, string? activeTab)
    {
        TraceLogger.Enter("DataInputFormats", nameof(Delete));
        var c = await _db.InputFormatConfigs.FindAsync(id);
        if (c != null) { _db.InputFormatConfigs.Remove(c); await _db.SaveChangesAsync(); }
        if (IsAjax) return Json(new { ok = true });
        TraceLogger.Exit("DataInputFormats", nameof(Delete));
        return RedirectToAction(nameof(Index), new { activeTab });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> GeneratePayloadAjax(int configId)
    {
        TraceLogger.Enter("DataInputFormats", nameof(GeneratePayloadAjax));
        var config = await _db.InputFormatConfigs.FindAsync(configId);
        if (config == null) return Json(new { ok = false, error = "Config not found" });

        var fields  = config.EnabledFieldsRaw?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
        var payload = _payloadSvc.Generate(config.SourceType, fields);
        var vc      = _classifier.Classify(payload, config.SourceType);
        var label   = $"{config.FormatName} — {DateTime.UtcNow:HH:mm:ss}";

        _db.SamplePayloads.Add(new SamplePayload
        {
            ConfigId    = configId,
            SourceType  = config.SourceType,
            Label       = label,
            Payload     = payload,
            IsValid     = true,
            CreatedDate = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        return Json(new {
            ok    = true,
            label,
            payload,
            classification = new {
                domain     = vc.Domain,
                category   = vc.Category,
                make       = vc.Make,
                color      = vc.Color,
                shape      = vc.Shape3D,
                confidence = vc.Confidence,
                isAir      = vc.Domain == "air"
            }
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DuplicateConfig(int id, string targetTab)
    {
        TraceLogger.Enter("DataInputFormats", nameof(DuplicateConfig));
        var original = await _db.InputFormatConfigs.FindAsync(id);
        if (original == null) return Json(new { ok = false, error = "Config not found" });
        var validTabs = new[] { "physical", "satellite", "telecom", "tracker", "airflycar" };
        if (!validTabs.Contains(targetTab)) return Json(new { ok = false, error = "Invalid target tab" });

        var copy = new InputFormatConfig
        {
            FormatName       = original.FormatName + " (copy)",
            SourceId         = original.SourceId + "-" + targetTab,
            SourceType       = targetTab,
            InputSource      = original.InputSource,
            Description      = original.Description,
            EnabledFieldsRaw = original.EnabledFieldsRaw,
            CreatedDate      = DateTime.UtcNow
        };
        _db.InputFormatConfigs.Add(copy);
        await _db.SaveChangesAsync();
        TraceLogger.Exit("DataInputFormats", nameof(DuplicateConfig));
        return Json(new { ok = true, targetTab, configId = copy.Id, name = copy.FormatName });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeletePayload(int id, string? activeTab)
    {
        TraceLogger.Enter("DataInputFormats", nameof(DeletePayload));
        var p = await _db.SamplePayloads.FindAsync(id);
        if (p != null) { _db.SamplePayloads.Remove(p); await _db.SaveChangesAsync(); }
        if (IsAjax) return Json(new { ok = true });
        TraceLogger.Exit("DataInputFormats", nameof(DeletePayload));
        return RedirectToAction(nameof(Index), new { activeTab });
    }

    /// <summary>
    /// Task 10: Ensure isAirFlyCar="N" is present in non-airflycar payload JSON before
    /// classification. Overrides any randomly-generated Y value from the payload service,
    /// preventing altitude-based events from being promoted to air by the Y-field gate.
    /// </summary>
    private static string ForceIsAirFlyCarN(string json)
    {
        try
        {
            var node = JsonNode.Parse(json)?.AsObject();
            if (node is null) return json;
            node["isAirFlyCar"] = "N";
            return node.ToJsonString();
        }
        catch { return json; }
    }
}