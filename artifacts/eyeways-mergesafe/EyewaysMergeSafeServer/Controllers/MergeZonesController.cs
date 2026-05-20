using EyewaysMergeSafeServer.Data;
using EyewaysMergeSafeServer.Models;
using EyewaysMergeSafeServer.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EyewaysMergeSafeServer.Controllers;

public class MergeZonesController : Controller
{
    private readonly AppDbContext _db;
    public MergeZonesController(AppDbContext db) { _db = db; }

    public async Task<IActionResult> Index(string? highwayId)
    {
        var highways = await _db.Highways.AsNoTracking().Where(h => h.IsActive).OrderBy(h => h.Name).ToListAsync();
        highwayId ??= HttpContext.Session.GetString("HighwayId") ?? highways.FirstOrDefault()?.HighwayId;
        if (highwayId != null) HttpContext.Session.SetString("HighwayId", highwayId);

        var zones = await _db.MergeZones.AsNoTracking()
            .Where(z => z.HighwayId == highwayId)
            .OrderBy(z => z.MileMarker)
            .ToListAsync();

        return View(new MergeZoneViewModel { Highways = highways, SelectedHighwayId = highwayId, Zones = zones });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(MergeZone model)
    {
        _db.MergeZones.Add(model);
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index), new { highwayId = model.HighwayId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(MergeZone model)
    {
        _db.MergeZones.Update(model);
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index), new { highwayId = model.HighwayId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, string? highwayId)
    {
        var z = await _db.MergeZones.FindAsync(id);
        if (z != null) { _db.MergeZones.Remove(z); await _db.SaveChangesAsync(); }
        return RedirectToAction(nameof(Index), new { highwayId });
    }
}
