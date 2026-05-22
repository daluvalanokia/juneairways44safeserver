using EyewaysMergeSafeServer.Data;
using EyewaysMergeSafeServer.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EyewaysMergeSafeServer.Controllers;

public class Traffic3DController : Controller
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _cfg;
    private readonly IMemoryCache _cache;
    private readonly IHttpClientFactory _httpFactory;

    private static readonly Regex _bboxRegex = new(
        @"^-?\d{1,3}(\.\d+)?,-?\d{1,3}(\.\d+)?,-?\d{1,3}(\.\d+)?,-?\d{1,3}(\.\d+)?$",
        RegexOptions.Compiled);

    public Traffic3DController(AppDbContext db, IConfiguration cfg, IMemoryCache cache, IHttpClientFactory httpFactory)
    { _db = db; _cfg = cfg; _cache = cache; _httpFactory = httpFactory; }

    public async Task<IActionResult> Index(string? highwayId)
    {
        var highways = await _db.Highways.AsNoTracking().Where(h => h.IsActive).OrderBy(h => h.Name).ToListAsync();
        highwayId ??= HttpContext.Session.GetString("HighwayId") ?? highways.FirstOrDefault()?.HighwayId;
        if (highwayId != null) HttpContext.Session.SetString("HighwayId", highwayId);

        var zones   = await _db.MergeZones.AsNoTracking().Where(z => z.HighwayId == highwayId).ToListAsync();
        var sensors = await _db.SensorDevices.AsNoTracking().Where(d => d.HighwayId == highwayId).ToListAsync();
        var zoneIds = zones.Select(z => z.ZoneId).ToList();
        var servers = await _db.SwitchServers.AsNoTracking()
            .Where(s => s.ZoneId != null && zoneIds.Contains(s.ZoneId))
            .OrderBy(s => s.ZoneId).ThenBy(s => s.ServerName)
            .ToListAsync();

        return View(new Traffic3DViewModel
        {
            Highways          = highways,
            SelectedHighwayId = highwayId,
            Zones             = zones,
            SwitchServers     = servers,
            Sensors           = sensors,
            TomTomApiKey      = _cfg["TomTomApiKey"]
        });
    }

    [HttpGet]
    public async Task<IActionResult> GetTrafficSegments(string highwayId, string? bbox)
    {
        if (!string.IsNullOrEmpty(bbox) && !_bboxRegex.IsMatch(bbox))
            return BadRequest("Invalid bbox parameter.");

        var cacheKey = $"traffic_{highwayId}_{bbox ?? ""}";
        if (_cache.TryGetValue(cacheKey, out object? cached))
            return Json(cached);

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
                var response = await client.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var parsed = JsonSerializer.Deserialize<object>(json);
                    segments = new { source = "tomtom", data = parsed };
                }
                else
                {
                    segments = BuildSimulatedSegments(highwayId);
                }
            }
            catch
            {
                segments = BuildSimulatedSegments(highwayId);
            }
        }
        else
        {
            segments = BuildSimulatedSegments(highwayId);
        }

        _cache.Set(cacheKey, segments, TimeSpan.FromMinutes(5));
        return Json(segments);
    }

    private static object BuildSimulatedSegments(string highwayId)
    {
        var rng = new Random();
        var segmentNames = highwayId == "I20-TX"
            ? new[] { "Dallas West", "Grand Prairie", "Arlington", "Fort Worth East", "Mesquite", "Duncanville", "DeSoto", "Lancaster" }
            : highwayId == "I35-TX"
            ? new[] { "Waco North", "Temple", "Georgetown", "Round Rock", "Austin North", "San Marcos", "New Braunfels", "San Antonio" }
            : new[] { "Houston West", "Katy", "Sugar Land", "Houston East", "Beaumont", "Orange", "Baytown", "Pasadena" };

        return new
        {
            source = "simulated",
            highway = highwayId,
            generatedAt = DateTime.UtcNow,
            segments = segmentNames.Select((name, i) => new
            {
                id = $"SEG-{(i + 1):D3}",
                name,
                speedMph = rng.Next(15, 75),
                freeFlowSpeedMph = 70,
                congestion = rng.Next(0, 5) switch { 4 => "heavy", 3 => "moderate", _ => "free" },
                travelTimeSeconds = rng.Next(60, 600)
            }).ToList()
        };
    }
}
