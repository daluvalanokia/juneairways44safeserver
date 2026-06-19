using AirwaysMergeSafeServer.Data;
using AirwaysMergeSafeServer.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AirwaysMergeSafeServer.Controllers;

/// <summary>
/// C1 FIX: Plain-text password fallback removed. BCrypt-only. Transparent work-factor upgrade.
/// </summary>
public class PortalController : Controller
{
    private const int    LockoutThreshold = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    private readonly AppDbContext              _db;
    private readonly ILogger<PortalController> _logger;

    public PortalController(AppDbContext db, ILogger<PortalController> logger)
    { _db = db; _logger = logger; }

    public async Task<IActionResult> Index()
    {
        if (HttpContext.Session.GetString("HighwayId") != null)
            return RedirectToAction("Index", "Dashboard");
        return View(new PortalViewModel
        {
            Highways = await _db.Highways.AsNoTracking().Where(h => h.IsActive).OrderBy(h => h.Name).ToListAsync()
        });
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

        if (user is null)
        {
            _logger.LogWarning("Security: Failed login — userId={UserId} highway={HighwayId} ip={Ip} reason=UserNotFound", userId, highwayId, ip);
            return InvalidCredentials(highways, highwayId, userId);
        }

        if (user.LockedUntil.HasValue && user.LockedUntil.Value > DateTime.UtcNow)
        {
            _logger.LogWarning("Security: Login blocked — locked userId={UserId} highway={HighwayId} ip={Ip} until={Until}", userId, highwayId, ip, user.LockedUntil.Value);
            return View("Index", new PortalViewModel
            {
                Highways = highways, SelectedHighwayId = highwayId, UserId = userId,
                Error = "Account is temporarily locked. Please try again later."
            });
        }

        // C1 FIX: BCrypt-only — no plaintext fallback
        bool passwordValid = VerifyAndUpgradePassword(user, password);

        if (!passwordValid)
        {
            user.FailedLoginAttempts++;
            if (user.FailedLoginAttempts >= LockoutThreshold)
            {
                user.LockedUntil = DateTime.UtcNow.Add(LockoutDuration);
                _logger.LogWarning("Security: Account locked — userId={UserId} highway={HighwayId} ip={Ip} attempts={Attempts}", userId, highwayId, ip, user.FailedLoginAttempts);
            }
            else
            {
                _logger.LogWarning("Security: Failed login — userId={UserId} highway={HighwayId} ip={Ip} attempts={Attempts}", userId, highwayId, ip, user.FailedLoginAttempts);
            }
            await _db.SaveChangesAsync();
            return InvalidCredentials(highways, highwayId, userId);
        }

        user.FailedLoginAttempts = 0;
        user.LockedUntil         = null;
        await _db.SaveChangesAsync();  // persists any BCrypt work-factor upgrade

        HttpContext.Session.Clear();
        HttpContext.Session.SetString("HighwayId", highwayId);
        HttpContext.Session.SetString("UserId",    userId);
        HttpContext.Session.SetString("UserType",  user.UserType);
        HttpContext.Session.SetString("FullName",  user.FullName);

        _logger.LogInformation("Security: Successful login — userId={UserId} highway={HighwayId} ip={Ip}", userId, highwayId, ip);
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

    private IActionResult InvalidCredentials(List<AirwaysMergeSafeServer.Models.Highway> highways, string highwayId, string userId)
        => View("Index", new PortalViewModel { Highways = highways, SelectedHighwayId = highwayId, UserId = userId, Error = "Invalid credentials." });

    /// <summary>
    /// BCrypt-only. Rejects any non-BCrypt stored value (C1).
    /// Transparently upgrades work factor if below 12 rounds.
    /// </summary>
    private static bool VerifyAndUpgradePassword(AirwaysMergeSafeServer.Models.UserProfile user, string supplied)
    {
        if (string.IsNullOrEmpty(user.Password)) return false;
        if (!user.Password.StartsWith("$2")) return false;   // reject plaintext — C1 closed

        bool valid = BCrypt.Net.BCrypt.Verify(supplied, user.Password);
        if (valid && BCrypt.Net.BCrypt.PasswordNeedsRehash(user.Password, 12))
            user.Password = BCrypt.Net.BCrypt.HashPassword(supplied, workFactor: 12);
        return valid;
    }
}
