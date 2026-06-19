using AirwaysMergeSafeServer.Data;
using AirwaysMergeSafeServer.Models;
using AirwaysMergeSafeServer.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AirwaysMergeSafeServer.Controllers;

/// <summary>
/// C4 FIX: ModelState.IsValid guards on Create/Edit.
/// A7 FIX: IsAjax pattern + JSON returns added for consistency with other CRUD controllers.
/// </summary>
public class TriangulationController : Controller
{
    private readonly AppDbContext _db;
    public TriangulationController(AppDbContext db) { _db = db; }
    private bool IsAjax => Request.Headers["X-Requested-With"] == "XMLHttpRequest";

    public async Task<IActionResult> Index(string? highwayId)
    {
        var highways = await _db.Highways.AsNoTracking().Where(h => h.IsActive).OrderBy(h => h.Name).ToListAsync();
        highwayId ??= HttpContext.Session.GetString("HighwayId") ?? highways.FirstOrDefault()?.HighwayId;
        if (highwayId != null) HttpContext.Session.SetString("HighwayId", highwayId);

        var configs = await _db.TriangulationConfigs.AsNoTracking()
            .Where(c => c.HighwayId == highwayId).OrderBy(c => c.ZoneId).ToListAsync();
        return View(new TriangulationViewModel { Highways = highways, SelectedHighwayId = highwayId, Configs = configs });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(TriangulationConfig model)
    {
        if (!ModelState.IsValid) // C4 + A7 FIX
        {
            if (IsAjax) return Json(new { ok = false, errors = ModelStateErrors() });
            return RedirectToAction(nameof(Index), new { highwayId = model.HighwayId });
        }
        _db.TriangulationConfigs.Add(model);
        await _db.SaveChangesAsync();
        if (IsAjax) return Json(new { ok = true, highwayId = model.HighwayId });
        return RedirectToAction(nameof(Index), new { highwayId = model.HighwayId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(TriangulationConfig model)
    {
        if (!ModelState.IsValid) // C4 + A7 FIX
        {
            if (IsAjax) return Json(new { ok = false, errors = ModelStateErrors() });
            return RedirectToAction(nameof(Index), new { highwayId = model.HighwayId });
        }
        _db.TriangulationConfigs.Update(model);
        await _db.SaveChangesAsync();
        if (IsAjax) return Json(new { ok = true, highwayId = model.HighwayId });
        return RedirectToAction(nameof(Index), new { highwayId = model.HighwayId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, string? highwayId)
    {
        var c = await _db.TriangulationConfigs.FindAsync(id);
        if (c != null) { _db.TriangulationConfigs.Remove(c); await _db.SaveChangesAsync(); }
        if (IsAjax) return Json(new { ok = true }); // A7 FIX: was missing
        return RedirectToAction(nameof(Index), new { highwayId });
    }

    private Dictionary<string, IEnumerable<string>> ModelStateErrors() =>
        ModelState.Where(e => e.Value?.Errors.Count > 0)
                  .ToDictionary(e => e.Key, e => e.Value!.Errors.Select(x => x.ErrorMessage));
}
