using EyewaysMergeSafeServer.Data;
using EyewaysMergeSafeServer.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EyewaysMergeSafeServer.Controllers;

public class PortalController : Controller
{
    private const int LockoutThreshold  = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    private readonly AppDbContext _db;
    private readonly ILogger<PortalController> _logger;

    public PortalController(AppDbContext db, ILogger<PortalController> logger)
    {
        _db     = db;
        _logger = logger;
    }

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

    [HttpGet("Portal/Login")]
    public IActionResult LoginGet() => RedirectToAction("Index");

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(string highwayId, string userId, string password)
    {
        var ip       = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var highways = await _db.Highways.AsNoTracking().Where(h => h.IsActive).OrderBy(h => h.Name).ToListAsync();

        var user = await _db.UserProfiles
            .FirstOrDefaultAsync(u => u.UserId == userId && u.HighwayId == highwayId && u.IsActive);

        if (user == null)
        {
            _logger.LogWarning("Security: Failed login — userId={UserId} highway={HighwayId} ip={Ip} reason=UserNotFound",
                userId, highwayId, ip);
            return InvalidCredentials(highways, highwayId, userId);
        }

        if (user.LockedUntil.HasValue && user.LockedUntil.Value > DateTime.UtcNow)
        {
            _logger.LogWarning("Security: Login blocked — account locked userId={UserId} highway={HighwayId} ip={Ip} lockedUntil={LockedUntil}",
                userId, highwayId, ip, user.LockedUntil.Value);
            return View("Index", new PortalViewModel
            {
                Highways          = highways,
                SelectedHighwayId = highwayId,
                UserId            = userId,
                Error             = "Account is temporarily locked. Please try again later."
            });
        }

        bool passwordValid = VerifyPassword(password, user.Password);

        if (!passwordValid)
        {
            user.FailedLoginAttempts++;
            if (user.FailedLoginAttempts >= LockoutThreshold)
            {
                user.LockedUntil = DateTime.UtcNow.Add(LockoutDuration);
                _logger.LogWarning("Security: Account locked — userId={UserId} highway={HighwayId} ip={Ip} attempts={Attempts}",
                    userId, highwayId, ip, user.FailedLoginAttempts);
            }
            else
            {
                _logger.LogWarning("Security: Failed login — userId={UserId} highway={HighwayId} ip={Ip} attempts={Attempts}",
                    userId, highwayId, ip, user.FailedLoginAttempts);
            }
            await _db.SaveChangesAsync();
            return InvalidCredentials(highways, highwayId, userId);
        }

        user.FailedLoginAttempts = 0;
        user.LockedUntil         = null;

        if (!string.IsNullOrEmpty(user.Password) && !user.Password.StartsWith("$2"))
        {
            user.Password = BCrypt.Net.BCrypt.HashPassword(password);
        }
        await _db.SaveChangesAsync();

        HttpContext.Session.Clear();
        HttpContext.Session.SetString("HighwayId", highwayId);
        HttpContext.Session.SetString("UserId",    userId);
        HttpContext.Session.SetString("UserType",  user.UserType);
        HttpContext.Session.SetString("FullName",  user.FullName);

        _logger.LogInformation("Security: Successful login — userId={UserId} highway={HighwayId} ip={Ip}",
            userId, highwayId, ip);

        return RedirectToAction("Index", "Dashboard");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult Logout()
    {
        var userId    = HttpContext.Session.GetString("UserId")    ?? "unknown";
        var highwayId = HttpContext.Session.GetString("HighwayId") ?? "unknown";
        _logger.LogInformation("Security: Logout — userId={UserId} highway={HighwayId}", userId, highwayId);
        HttpContext.Session.Clear();
        return RedirectToAction("Index");
    }

    private IActionResult InvalidCredentials(
        List<EyewaysMergeSafeServer.Models.Highway> highways,
        string highwayId,
        string userId)
        => View("Index", new PortalViewModel
        {
            Highways          = highways,
            SelectedHighwayId = highwayId,
            UserId            = userId,
            Error             = "Invalid credentials."
        });

    private static bool VerifyPassword(string supplied, string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return false;

        if (stored.StartsWith("$2"))
            return BCrypt.Net.BCrypt.Verify(supplied, stored);

        return supplied == stored;
    }
}
