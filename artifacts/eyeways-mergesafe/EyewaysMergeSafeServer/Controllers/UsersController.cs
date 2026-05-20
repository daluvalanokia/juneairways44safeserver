using EyewaysMergeSafeServer.Data;
using EyewaysMergeSafeServer.Models;
using EyewaysMergeSafeServer.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EyewaysMergeSafeServer.Controllers;

public class UsersController : Controller
{
    private readonly AppDbContext _db;
    public UsersController(AppDbContext db) { _db = db; }

    public async Task<IActionResult> Index(string? search)
    {
        var highways = await _db.Highways.AsNoTracking().Where(h => h.IsActive).OrderBy(h => h.Name).ToListAsync();
        var highwayId = HttpContext.Session.GetString("HighwayId");

        var query = _db.UserProfiles.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(u => u.FullName.Contains(search) || (u.UserId != null && u.UserId.Contains(search)));
        var users = await query.OrderBy(u => u.UserType).ThenBy(u => u.FullName).ToListAsync();
        var devices = await _db.SensorDevices.AsNoTracking().OrderBy(d => d.DeviceName).ToListAsync();

        return View(new UsersViewModel { Highways = highways, Users = users, Devices = devices, Search = search });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(UserProfile model, string[]? selectedDeviceIds)
    {
        model.DeviceIdsRaw = selectedDeviceIds != null ? string.Join(",", selectedDeviceIds) : null;
        _db.UserProfiles.Add(model);
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(UserProfile model, string[]? selectedDeviceIds)
    {
        model.DeviceIdsRaw = selectedDeviceIds != null ? string.Join(",", selectedDeviceIds) : null;
        if (string.IsNullOrEmpty(model.Password))
        {
            var existing = await _db.UserProfiles.AsNoTracking().FirstOrDefaultAsync(u => u.Id == model.Id);
            model.Password = existing?.Password;
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
