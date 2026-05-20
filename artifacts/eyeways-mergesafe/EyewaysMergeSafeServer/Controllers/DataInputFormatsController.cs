using EyewaysMergeSafeServer.Data;
using EyewaysMergeSafeServer.Models;
using EyewaysMergeSafeServer.Services;
using EyewaysMergeSafeServer.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace EyewaysMergeSafeServer.Controllers;

public class DataInputFormatsController : Controller
{
    private readonly AppDbContext        _db;
    private readonly InputPayloadService _payloadSvc;

    public DataInputFormatsController(AppDbContext db, InputPayloadService payloadSvc)
    { _db = db; _payloadSvc = payloadSvc; }

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
            SavedPayloads     = payloads,
            AllZones          = zones,
            AllSwitchServers  = srvs,
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SimulationPost(string? highwayId, string? zoneId, string? serverId, string? sourceType)
    {
        var type   = sourceType ?? "physical";
        var fields = new[] { "vehicle_id","timestamp","speed_mph","latitude","longitude",
                             "direction","lane","vehicle_type","event_type","zone_id",
                             "highway_id","signal_strength","altitude_ft" };
        var payload = _payloadSvc.Generate(type, fields);
        var label   = $"Simulation [{type.ToUpper()}] — {DateTime.UtcNow:HH:mm:ss}";
        var now     = DateTime.UtcNow;

        _db.SamplePayloads.Add(new SamplePayload
        {
            SourceType  = type,
            Label       = label,
            Payload     = payload,
            IsValid     = true,
            CreatedDate = now
        });

        // ── Also write a VehicleEvent so the 3D scene live feed picks it up ──
        try
        {
            using var doc  = System.Text.Json.JsonDocument.Parse(payload);
            var root       = doc.RootElement;
            string GetStr(string k) => root.TryGetProperty(k, out var v) ? (v.GetString() ?? "") : "";
            double? GetDbl(string k) => root.TryGetProperty(k, out var v) &&
                                        v.ValueKind == System.Text.Json.JsonValueKind.Number
                                        ? v.GetDouble() : null;

            var hw  = !string.IsNullOrEmpty(highwayId) ? highwayId : GetStr("highway_id");
            var zid = !string.IsNullOrEmpty(zoneId)    ? zoneId    : GetStr("zone_id");

            // Truncate payload to fit [MaxLength(500)]
            var shortPayload = payload.Length > 490 ? payload[..490] : payload;

            _db.VehicleEvents.Add(new VehicleEvent
            {
                EventType   = GetStr("event_type") is { Length: > 0 } et ? et : "detection",
                ZoneId      = zid,
                HighwayId   = hw,
                VehicleId   = "SIM-" + (GetStr("vehicle_id") is { Length: > 0 } vid ? vid : Guid.NewGuid().ToString("N")[..8]),
                SpeedMph    = GetDbl("speed_mph"),
                Latitude    = GetDbl("latitude"),
                Longitude   = GetDbl("longitude"),
                Payload     = shortPayload,
                CreatedDate = now
            });
        }
        catch { /* non-fatal: log to 3D scene is best-effort */ }

        await _db.SaveChangesAsync();
        return Json(new { ok = true, label, payload });
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
    public async Task<IActionResult> GeneratePayload(int configId)
    {
        var config = await _db.InputFormatConfigs.FindAsync(configId);
        if (config == null) return RedirectToAction(nameof(Index));

        var fields  = config.EnabledFieldsRaw?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
        var payload = _payloadSvc.Generate(config.SourceType, fields);
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
        return RedirectToAction(nameof(Index), new { activeTab = config.SourceType });
    }

    /// <summary>AJAX endpoint — returns JSON payload without page reload.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> GeneratePayloadAjax(int configId)
    {
        var config = await _db.InputFormatConfigs.FindAsync(configId);
        if (config == null)
            return Json(new { ok = false, error = "Config not found" });

        var fields  = config.EnabledFieldsRaw?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
        var payload = _payloadSvc.Generate(config.SourceType, fields);
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

        return Json(new { ok = true, label, payload });
    }

    /// <summary>
    /// Cross-tab duplicate: copy a config into a different source type tab.
    /// Returns JSON { ok, targetTab, configId, name }.
    /// </summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DuplicateConfig(int id, string targetTab)
    {
        var original = await _db.InputFormatConfigs.FindAsync(id);
        if (original == null) return Json(new { ok = false, error = "Config not found" });

        var validTabs = new[] { "physical", "satellite", "telecom", "tracker" };
        if (!validTabs.Contains(targetTab))
            return Json(new { ok = false, error = "Invalid target tab" });

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
}
