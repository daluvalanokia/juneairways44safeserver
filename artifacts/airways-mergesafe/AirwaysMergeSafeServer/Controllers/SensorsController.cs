using AirwaysMergeSafeServer.Data;
using AirwaysMergeSafeServer.Models;
using AirwaysMergeSafeServer.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AirwaysMergeSafeServer.Infrastructure;

namespace AirwaysMergeSafeServer.Controllers;

/// <summary>C4 FIX: ModelState.IsValid guards on Create/Edit.</summary>
public class SensorsController : Controller
{
    private readonly AppDbContext _db;
    public SensorsController(AppDbContext db) { _db = db; }
    private bool IsAjax => Request.Headers["X-Requested-With"] == "XMLHttpRequest";

    public async Task<IActionResult> Index(string? highwayId, string filterType = "all")
    {
        TraceLogger.Enter("Sensors", nameof(Index));
        var highways = await _db.Highways.AsNoTracking().Where(h => h.IsActive).OrderBy(h => h.Name).ToListAsync();
        highwayId ??= HttpContext.Session.GetString("HighwayId") ?? highways.FirstOrDefault()?.HighwayId;
        if (highwayId != null) HttpContext.Session.SetString("HighwayId", highwayId);

        var query = _db.SensorDevices.AsNoTracking().Where(d => d.HighwayId == highwayId);
        if (filterType != "all") query = query.Where(d => d.DeviceType == filterType);
        var sensors = await query.OrderBy(d => d.ZoneId).ThenBy(d => d.DeviceName).ToListAsync();
        TraceLogger.Exit("Sensors", nameof(Index));
        return View(new SensorViewModel { Highways = highways, SelectedHighwayId = highwayId, FilterType = filterType, Sensors = sensors });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(SensorDevice model)
    {
        TraceLogger.Enter("Sensors", nameof(Create));
        if (!ModelState.IsValid) // C4 FIX
        {
            if (IsAjax) return Json(new { ok = false, errors = ModelStateErrors() });
            return RedirectToAction(nameof(Index), new { highwayId = model.HighwayId });
        }
        _db.SensorDevices.Add(model);
        await _db.SaveChangesAsync();
        if (IsAjax) return Json(new { ok = true, highwayId = model.HighwayId });
        TraceLogger.Exit("Sensors", nameof(Create));
        return RedirectToAction(nameof(Index), new { highwayId = model.HighwayId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(SensorDevice model)
    {
        TraceLogger.Enter("Sensors", nameof(Edit));
        if (!ModelState.IsValid) // C4 FIX
        {
            if (IsAjax) return Json(new { ok = false, errors = ModelStateErrors() });
            return RedirectToAction(nameof(Index), new { highwayId = model.HighwayId });
        }
        _db.SensorDevices.Update(model);
        await _db.SaveChangesAsync();
        if (IsAjax) return Json(new { ok = true, highwayId = model.HighwayId });
        TraceLogger.Exit("Sensors", nameof(Edit));
        return RedirectToAction(nameof(Index), new { highwayId = model.HighwayId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, string? highwayId)
    {
        TraceLogger.Enter("Sensors", nameof(Delete));
        var d = await _db.SensorDevices.FindAsync(id);
        if (d != null) { _db.SensorDevices.Remove(d); await _db.SaveChangesAsync(); }
        if (IsAjax) return Json(new { ok = true });
        TraceLogger.Exit("Sensors", nameof(Delete));
        return RedirectToAction(nameof(Index), new { highwayId });
    }

    private Dictionary<string, IEnumerable<string>> ModelStateErrors() =>
        ModelState.Where(e => e.Value?.Errors.Count > 0)
                  .ToDictionary(e => e.Key, e => e.Value!.Errors.Select(x => x.ErrorMessage));
}