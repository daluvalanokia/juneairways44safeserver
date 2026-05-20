using EyewaysMergeSafeServer.Data;
using EyewaysMergeSafeServer.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EyewaysMergeSafeServer.Controllers;

public class PortalController : Controller
{
    private readonly AppDbContext _db;
    public PortalController(AppDbContext db) { _db = db; }

    public async Task<IActionResult> Index()
    {
        if (HttpContext.Session.GetString("HighwayId") != null)
            return RedirectToAction("Index", "Dashboard");

        var vm = new PortalViewModel
        {
            Highways = await _db.Highways.AsNoTracking().Where(h => h.IsActive).OrderBy(h => h.Name).ToListAsync()
        };
        return View(vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(string highwayId, string userId, string password)
    {
        var highways = await _db.Highways.AsNoTracking().Where(h => h.IsActive).OrderBy(h => h.Name).ToListAsync();

        var user = await _db.UserProfiles.AsNoTracking()
            .FirstOrDefaultAsync(u => u.UserId == userId && u.HighwayId == highwayId && u.IsActive);

        if (user == null)
        {
            return View("Index", new PortalViewModel
            {
                Highways = highways,
                SelectedHighwayId = highwayId,
                UserId = userId,
                Error = "User ID not found for the selected highway."
            });
        }

        if (!string.IsNullOrEmpty(user.Password) && user.Password != password)
        {
            return View("Index", new PortalViewModel
            {
                Highways = highways,
                SelectedHighwayId = highwayId,
                UserId = userId,
                Error = "Incorrect password."
            });
        }

        HttpContext.Session.SetString("HighwayId", highwayId);
        HttpContext.Session.SetString("UserId", userId);
        HttpContext.Session.SetString("UserType", user.UserType);
        HttpContext.Session.SetString("FullName", user.FullName);
        return RedirectToAction("Index", "Dashboard");
    }

    public IActionResult Logout()
    {
        HttpContext.Session.Clear();
        return RedirectToAction("Index");
    }
}
