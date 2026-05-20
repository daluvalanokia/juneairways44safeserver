using EyewaysMergeSafeServer.Data;
using EyewaysMergeSafeServer.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EyewaysMergeSafeServer.Controllers;

public class DashboardController : Controller
{
    private readonly AppDbContext _db;
    public DashboardController(AppDbContext db) { _db = db; }

    public async Task<IActionResult> Index(string? highwayId)
    {
        var highways = await _db.Highways.AsNoTracking().Where(h => h.IsActive).OrderBy(h => h.Name).ToListAsync();

        highwayId ??= HttpContext.Session.GetString("HighwayId") ?? highways.FirstOrDefault()?.HighwayId;
        if (highwayId != null) HttpContext.Session.SetString("HighwayId", highwayId);

        var zones   = await _db.MergeZones.AsNoTracking().Where(z => z.HighwayId == highwayId).ToListAsync();
        var servers = await _db.SwitchServers.AsNoTracking().Where(s => s.HighwayId == highwayId).ToListAsync();
        var sensors = await _db.SensorDevices.AsNoTracking().Where(d => d.HighwayId == highwayId).ToListAsync();
        var events  = await _db.VehicleEvents.AsNoTracking()
            .Where(e => e.HighwayId == highwayId)
            .OrderByDescending(e => e.CreatedDate)
            .Take(20)
            .ToListAsync();

        return View(new DashboardViewModel
        {
            Highways = highways,
            SelectedHighwayId = highwayId,
            Zones = zones,
            Servers = servers,
            Sensors = sensors,
            RecentEvents = events
        });
    }
}
