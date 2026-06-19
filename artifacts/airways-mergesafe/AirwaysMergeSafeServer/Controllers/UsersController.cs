using AirwaysMergeSafeServer.Data;
using AirwaysMergeSafeServer.Filters;
using AirwaysMergeSafeServer.Models;
using AirwaysMergeSafeServer.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AirwaysMergeSafeServer.Controllers;

/// <summary>
/// C4 FIX: ModelState.IsValid guard added to Create/Edit.
/// D1 FIX: UserType normalised to lowercase on write (aligns with EF converter).
/// </summary>
[AdminOnly]
public class UsersController : Controller
{
    private readonly AppDbContext _db;
    public UsersController(AppDbContext db) { _db = db; }

    public async Task<IActionResult> Index(string? search)
    {
        var highways  = await _db.Highways.AsNoTracking().Where(h => h.IsActive).OrderBy(h => h.Name).ToListAsync();
        var query     = _db.UserProfiles.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(u => u.FullName.Contains(search) || (u.UserId != null && u.UserId.Contains(search)));
        var users   = await query.OrderBy(u => u.UserType).ThenBy(u => u.FullName).ToListAsync();
        var devices = await _db.SensorDevices.AsNoTracking().OrderBy(d => d.DeviceName).ToListAsync();
        return View(new UsersViewModel { Highways = highways, Users = users, Devices = devices, Search = search });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(UserProfile model, string[]? selectedDeviceIds)
    {
        if (!ModelState.IsValid) return RedirectToAction(nameof(Index)); // C4 FIX
        model.DeviceIdsRaw = selectedDeviceIds != null ? string.Join(",", selectedDeviceIds) : null;
        model.UserType     = model.UserType?.ToLower() ?? "viewer"; // D1 FIX: normalise case
        if (!string.IsNullOrEmpty(model.Password) && !model.Password.StartsWith("$2"))
            model.Password = BCrypt.Net.BCrypt.HashPassword(model.Password, workFactor: 12);
        _db.UserProfiles.Add(model);
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(UserProfile model, string[]? selectedDeviceIds)
    {
        if (!ModelState.IsValid) return RedirectToAction(nameof(Index)); // C4 FIX
        model.DeviceIdsRaw = selectedDeviceIds != null ? string.Join(",", selectedDeviceIds) : null;
        model.UserType     = model.UserType?.ToLower() ?? "viewer"; // D1 FIX
        if (string.IsNullOrEmpty(model.Password))
        {
            var existing = await _db.UserProfiles.AsNoTracking().FirstOrDefaultAsync(u => u.Id == model.Id);
            model.Password = existing?.Password;
        }
        else if (!model.Password.StartsWith("$2"))
        {
            model.Password = BCrypt.Net.BCrypt.HashPassword(model.Password, workFactor: 12);
        }
        _db.UserProfiles.Update(model);
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var u = await _db.UserProfiles.FindAsync(id);
        if (u != null) { _db.UserProfiles.Remove(u); await _db.SaveChangesAsync(); }
        return RedirectToAction(nameof(Index));
    }
}
