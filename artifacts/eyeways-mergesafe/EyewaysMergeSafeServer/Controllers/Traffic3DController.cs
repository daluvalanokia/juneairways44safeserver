using EyewaysMergeSafeServer.Data;
using EyewaysMergeSafeServer.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace EyewaysMergeSafeServer.Controllers;

public class Traffic3DController : Controller
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _cfg;
    private readonly IMemoryCache _cache;
    public Traffic3DController(AppDbContext db, IConfiguration cfg, IMemoryCache cache)
    { _db = db; _cfg = cfg; _cache = cache; }

    public async Task<IActionResult> Index(string? highwayId)
    {
        var highways = await _db.Highways.AsNoTracking().Where(h => h.IsActive).OrderBy(h => h.Name).ToListAsync();
        highwayId ??= HttpContext.Session.GetString("HighwayId") ?? highways.FirstOrDefault()?.HighwayId;
        if (highwayId != null) HttpContext.Session.SetString("HighwayId", highwayId);

        var zones   = await _db.MergeZones.AsNoTracking().Where(z => z.HighwayId == highwayId).ToListAsync();
        var sensors = await _db.SensorDevices.AsNoTracking().Where(d => d.HighwayId == highwayId).ToListAsync();

        return View(new Traffic3DViewModel
        {
            Highways = highways,
            SelectedHighwayId = highwayId,
            Zones = zones,
            Sensors = sensors,
            TomTomApiKey = _cfg["TomTomApiKey"]
        });
    }

    [HttpGet]
    public IActionResult GetTrafficSegments(string highwayId)
    {
        var cacheKey = $"traffic_{highwayId}";
        if (!_cache.TryGetValue(cacheKey, out object? segments))
        {
            var rng = new Random();
            segments = Enumerable.Range(1, 8).Select(i => new
            {
                id = $"SEG-{i:D3}",
                name = $"Segment {i}",
                speed = rng.Next(15, 75),
                freeFlowSpeed = 70,
                congestion = rng.Next(0, 5) switch { 4 => "heavy", 3 => "moderate", _ => "free" }
            }).ToList();
            _cache.Set(cacheKey, segments, TimeSpan.FromMinutes(5));
        }
        return Json(segments);
    }
}
