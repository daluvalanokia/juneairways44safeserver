using AirwaysMergeSafeServer.Data;
using AirwaysMergeSafeServer.Models;
using AirwaysMergeSafeServer.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AirwaysMergeSafeServer.Controllers;

/// <summary>
/// A3 FIX: Feed endpoint now requires active session.
/// A5 FIX: EventsController logic consolidated here; EventsController deleted.
/// Phase 4: AltitudeMeters included in Feed projection.
/// </summary>
public class VehicleEventsController : Controller
{
    private readonly AppDbContext _db;

    private static readonly Func<AppDbContext, string, string, IAsyncEnumerable<VehicleEvent>> _eventsQuery =
        EF.CompileAsyncQuery((AppDbContext db, string hwId, string filter) =>
            db.VehicleEvents.AsNoTracking()
                .Where(e => e.HighwayId == hwId && (filter == "all" || e.EventType == filter))
                .OrderByDescending(e => e.CreatedDate)
                .Take(100));

    public VehicleEventsController(AppDbContext db) { _db = db; }

    public async Task<IActionResult> Index(string? highwayId, string filterType = "all")
    {
        var highways = await _db.Highways.AsNoTracking().Where(h => h.IsActive).OrderBy(h => h.Name).ToListAsync();
        highwayId ??= HttpContext.Session.GetString("HighwayId") ?? highways.FirstOrDefault()?.HighwayId;
        if (highwayId != null) HttpContext.Session.SetString("HighwayId", highwayId);

        var events = new List<VehicleEvent>();
        await foreach (var ev in _eventsQuery(_db, highwayId ?? "", filterType))
            events.Add(ev);

        return View(new EventsViewModel
        {
            Highways          = highways,
            SelectedHighwayId = highwayId,
            FilterType        = filterType,
            Events            = events
        });
    }

    // A3 FIX: session required; Phase 4: AltitudeMeters projected
    [HttpGet]
    public async Task<IActionResult> Feed(string? highwayId, string filterType = "all", int take = 50)
    {
        if (HttpContext.Session.GetString("UserId") is not { Length: > 0 })
            return Unauthorized(new { error = "Session required." });

        highwayId ??= HttpContext.Session.GetString("HighwayId") ?? "";
        take = Math.Clamp(take, 1, 200);

        var q = _db.VehicleEvents.AsNoTracking().Where(e => e.HighwayId == highwayId);
        if (filterType != "all") q = q.Where(e => e.EventType == filterType);

        var events = await q.OrderByDescending(e => e.CreatedDate).Take(take)
            .Select(e => new {
                e.EventType, e.ZoneId, e.VehicleId, e.SpeedMph,
                e.AltitudeMeters,   // Phase 4
                e.CreatedDate
            })
            .ToListAsync();
        return Json(events);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> InjectDemo(string zoneId, string highwayId, string eventType, double speedMph)
    {
        var rng = Random.Shared; // A1/A2 FIX: use Random.Shared
        _db.VehicleEvents.Add(new VehicleEvent
        {
            EventType   = eventType,
            ZoneId      = zoneId,
            HighwayId   = highwayId,
            VehicleId   = $"VEH-{rng.Next(1000, 9999)}",
            SpeedMph    = speedMph,
            Latitude    = 32.7 + rng.NextDouble() * 0.2,
            Longitude   = -97.0 + rng.NextDouble() * 0.2,
            AltitudeMeters = 50 + rng.NextDouble() * 100,  // Phase 4
            CreatedDate = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index), new { highwayId });
    }
}
