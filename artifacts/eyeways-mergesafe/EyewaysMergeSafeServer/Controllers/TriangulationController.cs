using EyewaysMergeSafeServer.Data;
using EyewaysMergeSafeServer.Models;
using EyewaysMergeSafeServer.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EyewaysMergeSafeServer.Controllers;

public class TriangulationController : Controller
{
    private readonly AppDbContext _db;
    public TriangulationController(AppDbContext db) { _db = db; }

    public async Task<IActionResult> Index(string? highwayId)
    {
        var highways = await _db.Highways.AsNoTracking().Where(h => h.IsActive).OrderBy(h => h.Name).ToListAsync();
        highwayId ??= HttpContext.Session.GetString("HighwayId") ?? highways.FirstOrDefault()?.HighwayId;
        if (highwayId != null) HttpContext.Session.SetString("HighwayId", highwayId);

        var configs = await _db.TriangulationConfigs.AsNoTracking()
            .Where(c => c.HighwayId == highwayId)
            .OrderBy(c => c.ZoneId)
            .ToListAsync();

        return View(new TriangulationViewModel { Highways = highways, SelectedHighwayId = highwayId, Configs = configs });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(TriangulationConfig model)
    {
        _db.TriangulationConfigs.Add(model);
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index), new { highwayId = model.HighwayId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(TriangulationConfig model)
    {
        _db.TriangulationConfigs.Update(model);
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index), new { highwayId = model.HighwayId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, string? highwayId)
    {
        var c = await _db.TriangulationConfigs.FindAsync(id);
        if (c != null) { _db.TriangulationConfigs.Remove(c); await _db.SaveChangesAsync(); }
        return RedirectToAction(nameof(Index), new { highwayId });
    }
}
