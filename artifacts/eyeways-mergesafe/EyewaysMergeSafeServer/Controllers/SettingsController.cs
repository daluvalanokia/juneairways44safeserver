using EyewaysMergeSafeServer.Data;
using EyewaysMergeSafeServer.Filters;
using EyewaysMergeSafeServer.Models;
using EyewaysMergeSafeServer.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace EyewaysMergeSafeServer.Controllers;

[AdminOnly]
public class SettingsController : Controller
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _cfg;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<SettingsController> _logger;

    public SettingsController(AppDbContext db, IConfiguration cfg, IWebHostEnvironment env, ILogger<SettingsController> logger)
    { _db = db; _cfg = cfg; _env = env; _logger = logger; }

    private static string PurgeSettingsPath =>
        Path.Combine(AppContext.BaseDirectory, "purgesettings.json");

    private static (int days, int count) LoadPurgeSettings()
    {
        try
        {
            if (System.IO.File.Exists(PurgeSettingsPath))
            {
                var d = JsonSerializer.Deserialize<Dictionary<string, int>>(
                    System.IO.File.ReadAllText(PurgeSettingsPath));
                return (d?.GetValueOrDefault("maxDays",  30)    ?? 30,
                        d?.GetValueOrDefault("maxCount", 10000) ?? 10000);
            }
        }
        catch { }
        return (30, 10000);
    }

    public async Task<IActionResult> Index()
    {
        var (maxDays, maxCount) = LoadPurgeSettings();
        return View(new SettingsViewModel
        {
            Highways      = await _db.Highways.AsNoTracking().OrderBy(h => h.Name).ToListAsync(),
            TomTomApiKey  = _cfg["TomTomApiKey"] ?? "",
            PurgeMaxDays  = maxDays,
            PurgeMaxCount = maxCount
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Highway model)
    {
        if (ModelState.IsValid)
        {
            model.CreatedDate = DateTime.UtcNow;
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
    public IActionResult SavePurgeSettings(int purgeMaxDays, int purgeMaxCount)
    {
        var userId = HttpContext.Session.GetString("UserId") ?? "unknown";
        _logger.LogInformation("Security: Admin action — SavePurgeSettings userId={UserId} maxDays={MaxDays} maxCount={MaxCount}",
            userId, purgeMaxDays, purgeMaxCount);

        var json = JsonSerializer.Serialize(new Dictionary<string, int>
        {
            ["maxDays"]  = Math.Max(1,   purgeMaxDays),
            ["maxCount"] = Math.Max(100, purgeMaxCount)
        }, new JsonSerializerOptions { WriteIndented = true });
        System.IO.File.WriteAllText(PurgeSettingsPath, json);
        TempData["PurgeSaved"] = "true";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RunPurge()
    {
        var userId = HttpContext.Session.GetString("UserId") ?? "unknown";
        _logger.LogInformation("Security: Admin action — RunPurge userId={UserId}", userId);

        var (maxDays, maxCount) = LoadPurgeSettings();

        var ageCutoff    = DateTime.UtcNow.AddDays(-maxDays);
        var deletedByAge = await _db.VehicleEvents
            .Where(e => e.CreatedDate < ageCutoff)
            .ExecuteDeleteAsync();

        var remaining     = await _db.VehicleEvents.CountAsync();
        var deletedByCount = 0;
        if (remaining > maxCount)
        {
            var excess = remaining - maxCount;
            var oldest = await _db.VehicleEvents
                              .OrderBy(e => e.CreatedDate)
                              .Take(excess)
                              .ToListAsync();
            _db.VehicleEvents.RemoveRange(oldest);
            await _db.SaveChangesAsync();
            deletedByCount = oldest.Count;
        }

        remaining = await _db.VehicleEvents.CountAsync();
        return Json(new
        {
            ok            = true,
            deletedByAge,
            deletedByCount,
            totalDeleted  = deletedByAge + deletedByCount,
            remaining,
            maxDays,
            maxCount
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult SaveTomTomKey(string apiKey)
    {
        var userId = HttpContext.Session.GetString("UserId") ?? "unknown";
        _logger.LogInformation("Security: Admin action — SaveTomTomKey userId={UserId}", userId);

        var keyFilePath = Path.Combine(AppContext.BaseDirectory, "tomtomkey.json");
        var payload = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["TomTomApiKey"] = apiKey?.Trim() ?? ""
        }, new JsonSerializerOptions { WriteIndented = true });
        System.IO.File.WriteAllText(keyFilePath, payload);

        var reloadable = _cfg as IConfigurationRoot;
        reloadable?.Reload();

        TempData["Success"] = "TomTom API key saved successfully.";
        return RedirectToAction(nameof(Index));
    }
}
