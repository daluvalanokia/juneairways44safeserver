using EyewaysMergeSafeServer.Data;
using EyewaysMergeSafeServer.Models;
using EyewaysMergeSafeServer.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EyewaysMergeSafeServer.Controllers;

public class SettingsController : Controller
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _cfg;
    public SettingsController(AppDbContext db, IConfiguration cfg) { _db = db; _cfg = cfg; }

    public async Task<IActionResult> Index()
    {
        return View(new SettingsViewModel
        {
            Highways = await _db.Highways.AsNoTracking().OrderBy(h => h.Name).ToListAsync(),
            TomTomApiKey = _cfg["TomTomApiKey"]
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Highway model)
    {
        if (ModelState.IsValid)
        {
            _db.Highways.Add(model);
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Highway model)
    {
        if (ModelState.IsValid)
        {
            _db.Highways.Update(model);
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var h = await _db.Highways.FindAsync(id);
        if (h != null) { _db.Highways.Remove(h); await _db.SaveChangesAsync(); }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult SaveTomTomKey(string apiKey)
    {
        TempData["KeySaved"] = true;
        return RedirectToAction(nameof(Index));
    }
}
