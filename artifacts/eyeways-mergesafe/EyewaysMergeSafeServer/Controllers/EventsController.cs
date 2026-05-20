using EyewaysMergeSafeServer.Data;
using EyewaysMergeSafeServer.Models;
using EyewaysMergeSafeServer.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EyewaysMergeSafeServer.Controllers;

public class EventsController : Controller
{
    private readonly AppDbContext _db;
    public EventsController(AppDbContext db) { _db = db; }

    public async Task<IActionResult> Index(string? highwayId, string filterType = "all")
    {
        var highways = await _db.Highways.AsNoTracking().Where(h => h.IsActive).OrderBy(h => h.Name).ToListAsync();
        highwayId ??= HttpContext.Session.GetString("HighwayId") ?? highways.FirstOrDefault()?.HighwayId;
        if (highwayId != null) HttpContext.Session.SetString("HighwayId", highwayId);

        var query = _db.VehicleEvents.AsNoTracking().Where(e => e.HighwayId == highwayId);
        if (filterType != "all") query = query.Where(e => e.EventType == filterType);
        var events = await query.OrderByDescending(e => e.CreatedDate).Take(100).ToListAsync();

        return View(new EventsViewModel { Highways = highways, SelectedHighwayId = highwayId, FilterType = filterType, Events = events });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> InjectDemo(string zoneId, string highwayId, string eventType, double speedMph)
    {
        var rng = new Random();
        _db.VehicleEvents.Add(new VehicleEvent
        {
            EventType = eventType,
            ZoneId = zoneId,
            HighwayId = highwayId,
            VehicleId = $"VEH-{rng.Next(1000, 9999)}",
            SpeedMph = speedMph,
            Latitude = 32.7 + rng.NextDouble() * 0.2,
            Longitude = -97.0 + rng.NextDouble() * 0.2,
            CreatedDate = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index), new { highwayId });
    }
}
