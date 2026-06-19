using AirwaysMergeSafeServer.Data;
using AirwaysMergeSafeServer.Models;
using AirwaysMergeSafeServer.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AirwaysMergeSafeServer.Controllers;

public class MergeZonesController : Controller
{
    private readonly AppDbContext _db;
    public MergeZonesController(AppDbContext db) { _db = db; }
    private bool IsAjax => Request.Headers["X-Requested-With"] == "XMLHttpRequest";

    public async Task<IActionResult> Index(string? highwayId)
    {
        var highways = await _db.Highways.AsNoTracking().Where(h => h.IsActive).OrderBy(h => h.Name).ToListAsync();
        highwayId ??= HttpContext.Session.GetString("HighwayId") ?? highways.FirstOrDefault()?.HighwayId;
        if (highwayId != null) HttpContext.Session.SetString("HighwayId", highwayId);

        var zones = await _db.MergeZones.AsNoTracking()
            .Where(z => z.HighwayId == highwayId).OrderBy(z => z.MileMarker).ToListAsync();
        return View(new MergeZoneViewModel { Highways = highways, SelectedHighwayId = highwayId, Zones = zones });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(MergeZone model)
    {
        if (!ModelState.IsValid) // C4 FIX
        {
            if (IsAjax) return Json(new { ok = false, errors = ModelStateErrors() });
            return RedirectToAction(nameof(Index), new { highwayId = model.HighwayId });
        }
        _db.MergeZones.Add(model);
        await _db.SaveChangesAsync();
        if (IsAjax) return Json(new { ok = true, highwayId = model.HighwayId });
        return RedirectToAction(nameof(Index), new { highwayId = model.HighwayId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(MergeZone model)
    {
        if (!ModelState.IsValid) // C4 FIX
        {
            if (IsAjax) return Json(new { ok = false, errors = ModelStateErrors() });
            return RedirectToAction(nameof(Index), new { highwayId = model.HighwayId });
        }
        _db.MergeZones.Update(model);
        await _db.SaveChangesAsync();
        if (IsAjax) return Json(new { ok = true, highwayId = model.HighwayId });
        return RedirectToAction(nameof(Index), new { highwayId = model.HighwayId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, string? highwayId)
    {
        var z = await _db.MergeZones.FindAsync(id);
        if (z != null) { _db.MergeZones.Remove(z); await _db.SaveChangesAsync(); }
        if (IsAjax) return Json(new { ok = true });
        return RedirectToAction(nameof(Index), new { highwayId });
    }

    private Dictionary<string, IEnumerable<string>> ModelStateErrors() =>
        ModelState.Where(e => e.Value?.Errors.Count > 0)
                  .ToDictionary(e => e.Key, e => e.Value!.Errors.Select(x => x.ErrorMessage));
}
