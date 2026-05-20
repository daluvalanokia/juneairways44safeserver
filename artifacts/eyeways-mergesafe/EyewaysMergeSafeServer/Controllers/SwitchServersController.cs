using EyewaysMergeSafeServer.Data;
using EyewaysMergeSafeServer.Models;
using EyewaysMergeSafeServer.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EyewaysMergeSafeServer.Controllers;

public class SwitchServersController : Controller
{
    private readonly AppDbContext _db;
    public SwitchServersController(AppDbContext db) { _db = db; }

    public async Task<IActionResult> Index(string? highwayId)
    {
        var highways = await _db.Highways.AsNoTracking().Where(h => h.IsActive).OrderBy(h => h.Name).ToListAsync();
        highwayId ??= HttpContext.Session.GetString("HighwayId") ?? highways.FirstOrDefault()?.HighwayId;
        if (highwayId != null) HttpContext.Session.SetString("HighwayId", highwayId);

        var servers = await _db.SwitchServers.AsNoTracking()
            .Where(s => s.HighwayId == highwayId)
            .OrderBy(s => s.ZoneId).ThenBy(s => s.ServerName)
            .ToListAsync();

        return View(new SwitchServerViewModel { Highways = highways, SelectedHighwayId = highwayId, Servers = servers });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(SwitchServer model)
    {
        _db.SwitchServers.Add(model);
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index), new { highwayId = model.HighwayId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(SwitchServer model)
    {
        _db.SwitchServers.Update(model);
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index), new { highwayId = model.HighwayId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, string? highwayId)
    {
        var s = await _db.SwitchServers.FindAsync(id);
        if (s != null) { _db.SwitchServers.Remove(s); await _db.SaveChangesAsync(); }
        return RedirectToAction(nameof(Index), new { highwayId });
    }
}
