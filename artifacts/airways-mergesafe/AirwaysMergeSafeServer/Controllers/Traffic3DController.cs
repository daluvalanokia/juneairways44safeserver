using AirwaysMergeSafeServer.Data;
using AirwaysMergeSafeServer.Services;
using AirwaysMergeSafeServer.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AirwaysMergeSafeServer.Controllers;

/// <summary>
/// Phase 7:
///   A1  — BuildSimulatedSegments uses Random.Shared (no more new Random()).
///   P6  — Index passes RecentEvents + ground/air counts to Traffic3DViewModel.
///   P6  — AirScene action enriches view with classified event breakdown.
/// </summary>
public class Traffic3DController : Controller
{
    private readonly AppDbContext        _db;
    private readonly IConfiguration      _cfg;
    private readonly IMemoryCache        _cache;
    private readonly IHttpClientFactory  _httpFactory;

    private static readonly Regex _bboxRegex = new(
        @"^-?\d{1,3}(\.\d+)?,-?\d{1,3}(\.\d+)?,-?\d{1,3}(\.\d+)?,-?\d{1,3}(\.\d+)?$",
        RegexOptions.Compiled);

    public Traffic3DController(AppDbContext db, IConfiguration cfg,
        IMemoryCache cache, IHttpClientFactory httpFactory)
    { _db = db; _cfg = cfg; _cache = cache; _httpFactory = httpFactory; }

    public async Task<IActionResult> Index(string? highwayId)
    {
        var highways  = await _db.Highways.AsNoTracking().Where(h => h.IsActive).OrderBy(h => h.Name).ToListAsync();
        highwayId ??= HttpContext.Session.GetString("HighwayId") ?? highways.FirstOrDefault()?.HighwayId;
        if (highwayId != null) HttpContext.Session.SetString("HighwayId", highwayId);

        var zones   = await _db.MergeZones.AsNoTracking().Where(z => z.HighwayId == highwayId).ToListAsync();
        var sensors = await _db.SensorDevices.AsNoTracking().Where(d => d.HighwayId == highwayId).ToListAsync();
        var zoneIds = zones.Select(z => z.ZoneId).ToList();
        var servers = await _db.SwitchServers.AsNoTracking()
            .Where(s => s.ZoneId != null && zoneIds.Contains(s.ZoneId))
            .OrderBy(s => s.ZoneId).ThenBy(s => s.ServerName).ToListAsync();

        // P6: recent events with classification data for scene bootstrap
        var recentEvents = await _db.VehicleEvents.AsNoTracking()
            .Where(e => e.HighwayId == highwayId)
            .OrderByDescending(e => e.CreatedDate)
            .Take(80)
            .Select(e => new {
                e.Id, e.VehicleId, e.EventType, e.ZoneId,
                e.SpeedMph, e.Latitude, e.Longitude, e.AltitudeMeters,
                e.VehicleMode, e.VehicleCategory, e.VehicleClassJson,
                e.CreatedDate
            })
            .ToListAsync();

        var groundCount = recentEvents.Count(e => e.VehicleMode == "ground");
        var airCount    = recentEvents.Count(e => e.VehicleMode == "air");

        return View(new Traffic3DViewModel
        {
            Highways          = highways,
            SelectedHighwayId = highwayId,
            Zones             = zones,
            SwitchServers     = servers,
            Sensors           = sensors,
            TomTomApiKey      = _cfg["TomTomApiKey"],
            RecentEventsJson  = JsonSerializer.Serialize(recentEvents),
            GroundCount       = groundCount,
            AirCount          = airCount
        });
    }

    [HttpGet]
    public async Task<IActionResult> AirScene(string? highwayId)
    {
        var highways = await _db.Highways.AsNoTracking().Where(h => h.IsActive).OrderBy(h => h.Name).ToListAsync();
        highwayId ??= HttpContext.Session.GetString("HighwayId") ?? highways.FirstOrDefault()?.HighwayId;

        var zones   = await _db.MergeZones.AsNoTracking().Where(z => z.HighwayId == highwayId).ToListAsync();
        var sensors = await _db.SensorDevices.AsNoTracking().Where(d => d.HighwayId == highwayId).ToListAsync();
        var zoneIds = zones.Select(z => z.ZoneId).ToList();
        var servers = await _db.SwitchServers.AsNoTracking()
            .Where(s => s.ZoneId != null && zoneIds.Contains(s.ZoneId))
            .OrderBy(s => s.ZoneId).ThenBy(s => s.ServerName).ToListAsync();

        // P6: air events + classified ground for comparison panel
        var recentEvents = await _db.VehicleEvents.AsNoTracking()
            .Where(e => e.HighwayId == highwayId)
            .OrderByDescending(e => e.CreatedDate)
            .Take(120)
            .Select(e => new {
                e.Id, e.VehicleId, e.EventType, e.ZoneId,
                e.SpeedMph, e.Latitude, e.Longitude, e.AltitudeMeters,
                e.VehicleMode, e.VehicleCategory, e.VehicleClassJson,
                e.CreatedDate
            })
            .ToListAsync();

        var groundCount = recentEvents.Count(e => e.VehicleMode == "ground");
        var airCount    = recentEvents.Count(e => e.VehicleMode == "air");

        // Category breakdown for the inspector legend
        var catBreakdown = recentEvents
            .GroupBy(e => e.VehicleCategory)
            .ToDictionary(g => g.Key, g => g.Count());

        return View(new Traffic3DViewModel
        {
            Highways          = highways,
            SelectedHighwayId = highwayId,
            Zones             = zones,
            SwitchServers     = servers,
            Sensors           = sensors,
            TomTomApiKey      = _cfg["TomTomApiKey"],
            RecentEventsJson  = JsonSerializer.Serialize(recentEvents),
            GroundCount       = groundCount,
            AirCount          = airCount,
            CategoryBreakdown = catBreakdown
        });
    }

    [HttpGet]
    public async Task<IActionResult> GetTrafficSegments(string highwayId, string? bbox)
    {
        if (!string.IsNullOrEmpty(bbox) && !_bboxRegex.IsMatch(bbox))
            return BadRequest("Invalid bbox parameter.");

        var cacheKey = $"traffic_{highwayId}_{bbox ?? ""}";
        if (_cache.TryGetValue(cacheKey, out object? cached)) return Json(cached);

        var tomTomKey = _cfg["TomTomApiKey"];
        object segments;

        if (!string.IsNullOrWhiteSpace(tomTomKey))
        {
            try
            {
                var client = _httpFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(8);
                var resolvedBbox = bbox ?? (highwayId == "I20-TX"
                    ? "32.72,-97.15,32.80,-96.95"
                    : highwayId == "I35-TX"
                    ? "31.05,-97.38,31.60,-97.08"
                    : "29.70,-95.80,29.90,-95.30");

                if (!_bboxRegex.IsMatch(resolvedBbox))
                    return BadRequest("Invalid bbox parameter.");

                var url = $"https://api.tomtom.com/traffic/services/4/flowSegmentData/absolute/10/json?key={tomTomKey}&bbox={resolvedBbox}";
                var resp = await client.GetAsync(url);
                segments = resp.IsSuccessStatusCode
                    ? new { source = "tomtom", data = JsonSerializer.Deserialize<object>(await resp.Content.ReadAsStringAsync()) }
                    : BuildSimulatedSegments(highwayId);
            }
            catch { segments = BuildSimulatedSegments(highwayId); }
        }
        else
        {
            segments = BuildSimulatedSegments(highwayId);
        }

        _cache.Set(cacheKey, segments, TimeSpan.FromMinutes(5));
        return Json(segments);
    }

    // A1 FIX: Random.Shared
    private static object BuildSimulatedSegments(string highwayId)
    {
        var rng  = Random.Shared;
        var names = highwayId == "I20-TX"
            ? new[] { "Dallas West","Grand Prairie","Arlington","Fort Worth East","Mesquite","Duncanville","DeSoto","Lancaster" }
            : highwayId == "I35-TX"
            ? new[] { "Waco North","Temple","Georgetown","Round Rock","Austin North","San Marcos","New Braunfels","San Antonio" }
            : new[] { "Houston West","Katy","Sugar Land","Houston East","Beaumont","Orange","Baytown","Pasadena" };

        return new {
            source      = "simulated",
            highway     = highwayId,
            generatedAt = DateTime.UtcNow,
            segments    = names.Select((name, i) => new {
                id                = $"SEG-{i+1:D3}",
                name,
                speedMph          = rng.Next(15, 75),
                freeFlowSpeedMph  = 70,
                congestion        = rng.Next(0, 5) switch { 4=>"heavy", 3=>"moderate", _=>"free" },
                travelTimeSeconds = rng.Next(60, 600)
            }).ToList()
        };
    }
}
