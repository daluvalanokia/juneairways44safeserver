using EyewaysMergeSafeServer.Data;
using EyewaysMergeSafeServer.Models;
using EyewaysMergeSafeServer.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace EyewaysMergeSafeServer.Controllers;

public class DataInputFormatsController : Controller
{
    private readonly AppDbContext _db;
    public DataInputFormatsController(AppDbContext db) { _db = db; }

    public async Task<IActionResult> Index(string activeTab = "physical")
    {
        var highways = await _db.Highways.AsNoTracking().Where(h => h.IsActive).OrderBy(h => h.Name).ToListAsync();
        var highwayId = HttpContext.Session.GetString("HighwayId");

        var allConfigs = await _db.InputFormatConfigs.AsNoTracking().OrderBy(c => c.FormatName).ToListAsync();
        var payloads = await _db.SamplePayloads.AsNoTracking().OrderByDescending(p => p.CreatedDate).Take(30).ToListAsync();

        return View(new DataInputFormatsViewModel
        {
            Highways = highways,
            SelectedHighwayId = highwayId,
            ActiveTab = activeTab,
            PhysicalConfigs  = allConfigs.Where(c => c.SourceType == "physical").ToList(),
            SatelliteConfigs = allConfigs.Where(c => c.SourceType == "satellite").ToList(),
            TelecomConfigs   = allConfigs.Where(c => c.SourceType == "telecom").ToList(),
            TrackerConfigs   = allConfigs.Where(c => c.SourceType == "tracker").ToList(),
            SavedPayloads    = payloads,
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(InputFormatConfig model, string[] enabledFields, string[]? customFieldNames, string[]? customFieldTypes)
    {
        var combined = enabledFields.ToList();
        if (customFieldNames != null)
            combined.AddRange(customFieldNames.Where(n => !string.IsNullOrWhiteSpace(n)));
        model.EnabledFieldsRaw = string.Join(",", combined);
        _db.InputFormatConfigs.Add(model);
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index), new { activeTab = model.SourceType });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(InputFormatConfig model, string[] enabledFields, string[]? customFieldNames, string[]? customFieldTypes)
    {
        var combined = enabledFields.ToList();
        if (customFieldNames != null)
            combined.AddRange(customFieldNames.Where(n => !string.IsNullOrWhiteSpace(n)));
        model.EnabledFieldsRaw = string.Join(",", combined);
        _db.InputFormatConfigs.Update(model);
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index), new { activeTab = model.SourceType });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, string? activeTab)
    {
        var c = await _db.InputFormatConfigs.FindAsync(id);
        if (c != null) { _db.InputFormatConfigs.Remove(c); await _db.SaveChangesAsync(); }
        return RedirectToAction(nameof(Index), new { activeTab });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> GeneratePayload(int configId)
    {
        var config = await _db.InputFormatConfigs.FindAsync(configId);
        if (config == null) return RedirectToAction(nameof(Index));

        var fields = config.EnabledFieldsRaw?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
        var rng = new Random();
        var obj = new Dictionary<string, object?>();

        foreach (var f in fields)
        {
            obj[f] = f switch
            {
                "vehicle_id"   => $"VEH-{rng.Next(1000, 9999)}",
                "timestamp"    => DateTime.UtcNow.ToString("o"),
                "speed_mph"    => rng.Next(20, 100),
                "latitude"     => Math.Round(32.7 + rng.NextDouble() * 0.2, 6),
                "longitude"    => Math.Round(-96.9 + rng.NextDouble() * 0.3, 6),
                "direction"    => rng.Next(0, 360),
                "lane"         => rng.Next(1, 5),
                "vehicle_type" => new[] { "sedan", "suv", "truck", "motorcycle", "van" }[rng.Next(5)],
                "event_type"   => new[] { "detection", "merge", "speeding" }[rng.Next(3)],
                "zone_id"      => $"ZONE-{rng.Next(1, 10):D3}",
                "highway_id"   => "I20-TX",
                "signal_strength" => rng.Next(-80, -30),
                "altitude_ft"  => rng.Next(500, 700),
                "heading"      => rng.Next(0, 360),
                _              => $"value_{rng.Next(100, 999)}"
            };
        }

        var payload = JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true });

        _db.SamplePayloads.Add(new SamplePayload
        {
            ConfigId   = configId,
            SourceType = config.SourceType,
            Label      = $"{config.FormatName} — {DateTime.UtcNow:HH:mm:ss}",
            Payload    = payload,
            IsValid    = true,
            CreatedDate = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index), new { activeTab = config.SourceType });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeletePayload(int id, string? activeTab)
    {
        var p = await _db.SamplePayloads.FindAsync(id);
        if (p != null) { _db.SamplePayloads.Remove(p); await _db.SaveChangesAsync(); }
        return RedirectToAction(nameof(Index), new { activeTab });
    }
}
