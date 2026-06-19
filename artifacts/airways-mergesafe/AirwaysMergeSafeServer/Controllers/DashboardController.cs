using AirwaysMergeSafeServer.Data;
using AirwaysMergeSafeServer.Models;
using AirwaysMergeSafeServer.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;

namespace AirwaysMergeSafeServer.Controllers;

/// <summary>
/// A8 FIX: highwayId from query-string validated against the session user's permitted HighwayId.
///         An operator can only view their own highway; admins can view any.
/// </summary>
public class DashboardController : Controller
{
    private readonly AppDbContext _db;

    private static readonly Func<AppDbContext, string, IAsyncEnumerable<MergeZone>>    _zonesQuery =
        EF.CompileAsyncQuery((AppDbContext db, string hwId) =>
            db.MergeZones.AsNoTracking().Where(z => z.HighwayId == hwId));

    private static readonly Func<AppDbContext, string, IAsyncEnumerable<SwitchServer>> _serversQuery =
        EF.CompileAsyncQuery((AppDbContext db, string hwId) =>
            db.SwitchServers.AsNoTracking().Where(s => s.HighwayId == hwId));

    private static readonly Func<AppDbContext, string, IAsyncEnumerable<SensorDevice>> _sensorsQuery =
        EF.CompileAsyncQuery((AppDbContext db, string hwId) =>
            db.SensorDevices.AsNoTracking().Where(d => d.HighwayId == hwId));

    private static readonly Func<AppDbContext, string, IAsyncEnumerable<VehicleEvent>> _eventsQuery =
        EF.CompileAsyncQuery((AppDbContext db, string hwId) =>
            db.VehicleEvents.AsNoTracking()
                .Where(e => e.HighwayId == hwId)
                .OrderByDescending(e => e.CreatedDate)
                .Take(20));

    public DashboardController(AppDbContext db) { _db = db; }

    public async Task<IActionResult> Index(string? highwayId)
    {
        var highways = await _db.Highways.AsNoTracking()
            .Where(h => h.IsActive).OrderBy(h => h.Name).ToListAsync();

        // A8 FIX: validate requested highwayId against the user's permitted highway
        var sessionHighwayId = HttpContext.Session.GetString("HighwayId");
        var userType         = HttpContext.Session.GetString("UserType") ?? "viewer";
        var isAdmin          = userType.Equals("admin", StringComparison.OrdinalIgnoreCase);

        if (highwayId != null && !isAdmin && highwayId != sessionHighwayId)
        {
            // Non-admins cannot view other highways — silently clamp to their own
            highwayId = sessionHighwayId;
        }

        highwayId ??= sessionHighwayId ?? highways.FirstOrDefault()?.HighwayId;
        if (highwayId != null) HttpContext.Session.SetString("HighwayId", highwayId);

        var zones   = new List<MergeZone>();
        var servers = new List<SwitchServer>();
        var sensors = new List<SensorDevice>();
        var events  = new List<VehicleEvent>();

        if (highwayId != null)
        {
            await foreach (var z in _zonesQuery(_db, highwayId))   zones.Add(z);
            await foreach (var s in _serversQuery(_db, highwayId)) servers.Add(s);
            await foreach (var d in _sensorsQuery(_db, highwayId)) sensors.Add(d);
            await foreach (var e in _eventsQuery(_db, highwayId))  events.Add(e);
        }

        return View(new DashboardViewModel
        {
            Highways          = highways,
            SelectedHighwayId = highwayId,
            Zones             = zones,
            Servers           = servers,
            Sensors           = sensors,
            RecentEvents      = events
        });
    }

    [HttpGet, OutputCache(PolicyName = "ShortLive")]
    public async Task<IActionResult> MapMarkers(string? highwayId)
    {
        highwayId ??= HttpContext.Session.GetString("HighwayId") ?? "";

        var zones = await _db.MergeZones.AsNoTracking()
            .Where(z => z.HighwayId == highwayId && z.Latitude.HasValue)
            .Select(z => new { z.ZoneName, z.ZoneId, z.Status, lat = z.Latitude, lon = z.Longitude, r = z.GeofenceRadius })
            .ToListAsync();

        var sensors = await _db.SensorDevices.AsNoTracking()
            .Where(d => d.HighwayId == highwayId && d.Latitude.HasValue)
            .Select(d => new { d.DeviceName, d.DeviceId, d.DeviceType, d.Status, lat = d.Latitude, lon = d.Longitude })
            .ToListAsync();

        return Json(new { zones, sensors });
    }
}
