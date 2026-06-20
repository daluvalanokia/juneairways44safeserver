using AirwaysMergeSafeServer.Data;
using AirwaysMergeSafeServer.Models;
using AirwaysMergeSafeServer.Services;
using AirwaysMergeSafeServer.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Nodes;

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

    public DataInputFormatsController(
        AppDbContext        db,
        InputPayloadService payloadSvc,
        VehicleClassifier   classifier)
    { _db = db; _payloadSvc = payloadSvc; _classifier = classifier; }

    private bool IsAjax => Request.Headers["X-Requested-With"] == "XMLHttpRequest";

    public async Task<IActionResult> Index(string activeTab = "physical")
    {
        var highways   = await _db.Highways.AsNoTracking().Where(h => h.IsActive).OrderBy(h => h.Name).ToListAsync();
        var highwayId  = HttpContext.Session.GetString("HighwayId");
        var allConfigs = await _db.InputFormatConfigs.AsNoTracking().OrderBy(c => c.FormatName).ToListAsync();
        var payloads   = await _db.SamplePayloads.AsNoTracking().OrderByDescending(p => p.CreatedDate).Take(30).ToListAsync();
        var zones      = await _db.MergeZones.AsNoTracking().OrderBy(z => z.HighwayId).ThenBy(z => z.ZoneName).ToListAsync();
        var zoneIds    = zones.Select(z => z.ZoneId).ToList();
        var srvs       = await _db.SwitchServers.AsNoTracking()
                            .Where(s => s.ZoneId != null && zoneIds.Contains(s.ZoneId))
                            .OrderBy(s => s.ServerName).ToListAsync();

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
        var type          = sourceType ?? "physical";
        var isAirFlyCarSrc = string.Equals(type, "airflycar", StringComparison.OrdinalIgnoreCase);
        // Task 10: all formats carry isAirFlyCar explicitly (Y for airflycar source, N for others)
        var fields = isAirFlyCarSrc
            ? new[] {
                "vehicle_id","timestamp","latitude","longitude","altitude_m","speed_mph","heading",
                "vehicle_type","flight_phase","vertical_rate_fpm","battery_soc","battery_temp_c",
                "range_remaining_km","rotor_rpm","rotor_health","motor_temp_c","noise_db",
                "corridor_id","corridor_deviation_m","conflict_flag","separation_m",
                "passenger_count","destination_pad","pilot_id","icao_address","squawk",
                "zone_id","highway_id","event_type","isAirFlyCar","nic","nac_p"
              }
            : new[] {
                "vehicle_id","timestamp","speed_mph","latitude","longitude",
                "altitude_m","direction","lane","vehicle_type","event_type",
                "zone_id","highway_id","signal_strength","isAirFlyCar"
              };

        var payload = _payloadSvc.Generate(type, fields);
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

        // Write classified VehicleEvent
        try
        {
            using var doc  = JsonDocument.Parse(payload);
            var root       = doc.RootElement;
            string GetStr(string k) => root.TryGetProperty(k, out var v) ? (v.GetString() ?? "") : "";
            double? GetDbl(string k) => root.TryGetProperty(k, out var v) &&
                                        v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

            var hw  = !string.IsNullOrEmpty(highwayId) ? highwayId : GetStr("highway_id");
            // Always use the form zoneId (real DB zone) — never the random payload zone_id
            var zid = !string.IsNullOrEmpty(zoneId) ? zoneId : "";
            // Build stable vehicle id: SIM-VEH-001 etc (pool of 20 from payload)
            var rawVid = GetStr("vehicle_id") is { Length: > 0 } vp ? vp : Guid.NewGuid().ToString("N")[..8];
            var savedVehicleId = $"SIM-{rawVid}";
            var et  = GetStr("event_type") is { Length: > 0 } e ? e : "detection";

            // Task 10: determine IsAirFlyCar — forced "Y" for airflycar source, or if payload field set
            var iafRaw = GetStr("isAirFlyCar");
            var isAirFlyCarVal = (string.Equals(type, "airflycar", StringComparison.OrdinalIgnoreCase)
                || string.Equals(iafRaw, "Y", StringComparison.OrdinalIgnoreCase)) ? "Y" : "N";

            _db.VehicleEvents.Add(new VehicleEvent
            {
                EventType        = et,
                ZoneId           = zid,
                HighwayId        = hw,
                VehicleId        = savedVehicleId,
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
            });
        }
        catch { /* non-fatal */ }

        await _db.SaveChangesAsync();

        // Return classification in response so JS can update the scene immediately
        return Json(new {
            ok    = true,
            label,
            payload,
            // vehicleId returned so the 3D scene JS can match meshes by stable id
            vehicleId = savedVehicleId,
            classification = new {
                domain      = vc.Domain,
                category    = vc.Category,
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

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(InputFormatConfig model, string[] enabledFields, string[]? customFieldNames)
    {
        var combined = enabledFields.ToList();
        if (customFieldNames != null) combined.AddRange(customFieldNames.Where(n => !string.IsNullOrWhiteSpace(n)));
        model.EnabledFieldsRaw = string.Join(",", combined);
        _db.InputFormatConfigs.Add(model);
        await _db.SaveChangesAsync();
        if (IsAjax) return Json(new { ok = true, activeTab = model.SourceType });
        return RedirectToAction(nameof(Index), new { activeTab = model.SourceType });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(InputFormatConfig model, string[] enabledFields, string[]? customFieldNames)
    {
        var combined = enabledFields.ToList();
        if (customFieldNames != null) combined.AddRange(customFieldNames.Where(n => !string.IsNullOrWhiteSpace(n)));
        model.EnabledFieldsRaw = string.Join(",", combined);
        _db.InputFormatConfigs.Update(model);
        await _db.SaveChangesAsync();
        if (IsAjax) return Json(new { ok = true, activeTab = model.SourceType });
        return RedirectToAction(nameof(Index), new { activeTab = model.SourceType });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, string? activeTab)
    {
        var c = await _db.InputFormatConfigs.FindAsync(id);
        if (c != null) { _db.InputFormatConfigs.Remove(c); await _db.SaveChangesAsync(); }
        if (IsAjax) return Json(new { ok = true });
        return RedirectToAction(nameof(Index), new { activeTab });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> GeneratePayloadAjax(int configId)
    {
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
        return Json(new { ok = true, targetTab, configId = copy.Id, name = copy.FormatName });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeletePayload(int id, string? activeTab)
    {
        var p = await _db.SamplePayloads.FindAsync(id);
        if (p != null) { _db.SamplePayloads.Remove(p); await _db.SaveChangesAsync(); }
        if (IsAjax) return Json(new { ok = true });
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
