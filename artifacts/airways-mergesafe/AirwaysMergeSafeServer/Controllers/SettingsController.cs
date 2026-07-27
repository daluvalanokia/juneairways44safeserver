using AirwaysMergeSafeServer.Data;
using AirwaysMergeSafeServer.Filters;
using AirwaysMergeSafeServer.Models;
using AirwaysMergeSafeServer.Services;
using AirwaysMergeSafeServer.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using AirwaysMergeSafeServer.Infrastructure;

namespace AirwaysMergeSafeServer.Controllers;

[AdminOnly]
public class SettingsController : Controller
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _cfg;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<SettingsController> _logger;
    private readonly InAppTraceService _trace;

    public SettingsController(AppDbContext db, IConfiguration cfg, IWebHostEnvironment env,
        ILogger<SettingsController> logger, InAppTraceService trace)
    { _db = db; _cfg = cfg; _env = env; _logger = logger; _trace = trace; }

    private static string PurgeSettingsPath =>
        Path.Combine(AppContext.BaseDirectory, "purgesettings.json");

    private static string InAppTracePath =>
        Path.Combine(AppContext.BaseDirectory, "inapptrace.json");

    private static (bool enabled, string level) LoadInAppTraceSettings()
    {
        try
        {
            if (System.IO.File.Exists(InAppTracePath))
            {
                var d = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                    System.IO.File.ReadAllText(InAppTracePath));
                bool en = false;
                string lv = "info";
                if (d != null)
                {
                    if (d.TryGetValue("enabled", out var eEl) && eEl.ValueKind == JsonValueKind.True) en = true;
                    if (d.TryGetValue("level",   out var lEl) && lEl.ValueKind == JsonValueKind.String) lv = lEl.GetString() ?? "info";
                }
                return (en, lv);
            }
        }
        catch { }
        return (false, "info");
    }

    private static string AirSceneAlertsPath =>
        Path.Combine(AppContext.BaseDirectory, "airscenealerts.json");

    private const string DefaultAirSceneAlertsJson =
        """{"speedBandLimit":5,"defaultPattern":"circles","prevColorOverlay":false,"safeLabel":"Safe Speed","safeColor":"#22c55e","safeBands":1,"warningLabel":"Warning","warningColor":"#f59e0b","warningBands":2,"overspeedLabel":"Overspeed","overspeedColor":"#ef4444","overspeedBands":4}""";

    public static string LoadAirSceneAlertsJson()
    {
        try
        {
            if (System.IO.File.Exists(AirSceneAlertsPath))
                return System.IO.File.ReadAllText(AirSceneAlertsPath);
        }
        catch { }
        return DefaultAirSceneAlertsJson;
    }

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
        TraceLogger.Enter("Settings", nameof(Index));
        var (maxDays, maxCount) = LoadPurgeSettings();
        var (traceEnabled, traceLevel) = LoadInAppTraceSettings();
        TraceLogger.Exit("Settings", nameof(Index));
        return View(new SettingsViewModel
        {
            Highways           = await _db.Highways.AsNoTracking().OrderBy(h => h.Name).ToListAsync(),
            TomTomApiKey       = _cfg["TomTomApiKey"] ?? "",
            PurgeMaxDays       = maxDays,
            PurgeMaxCount      = maxCount,
            AirSceneAlertsJson = LoadAirSceneAlertsJson(),
            InAppTraceEnabled  = traceEnabled,
            InAppTraceLevel    = traceLevel
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult SaveAirSceneAlerts(
        int speedBandLimit, string defaultPattern, bool prevColorOverlay,
        string safeLabel, string safeColor, int safeBands,
        string warningLabel, string warningColor, int warningBands,
        string overspeedLabel, string overspeedColor, int overspeedBands)
    {
        TraceLogger.Enter("Settings", nameof(SaveAirSceneAlerts));
        var userId = HttpContext.Session.GetString("UserId") ?? "unknown";
        _logger.LogInformation("Security: Admin action — SaveAirSceneAlerts userId={UserId}", userId);

        var json = JsonSerializer.Serialize(new {
            speedBandLimit   = Math.Max(1, speedBandLimit),
            defaultPattern   = defaultPattern is "circles" or "squares" ? defaultPattern : "circles",
            prevColorOverlay,
            safeLabel        = safeLabel     ?? "Safe Speed",
            safeColor        = safeColor     ?? "#22c55e",
            safeBands        = Math.Max(1, Math.Min(10, safeBands)),
            warningLabel     = warningLabel  ?? "Warning",
            warningColor     = warningColor  ?? "#f59e0b",
            warningBands     = Math.Max(1, Math.Min(10, warningBands)),
            overspeedLabel   = overspeedLabel ?? "Overspeed",
            overspeedColor   = overspeedColor ?? "#ef4444",
            overspeedBands   = Math.Max(1, Math.Min(10, overspeedBands))
        }, new JsonSerializerOptions { WriteIndented = true });

        System.IO.File.WriteAllText(AirSceneAlertsPath, json);
        TempData["AlertSaved"] = "true";
        TraceLogger.Exit("Settings", nameof(SaveAirSceneAlerts));
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult SaveInAppTrace(bool enabled, string level)
    {
        TraceLogger.Enter("Settings", nameof(SaveInAppTrace));
        level = level is "info" or "warning" or "error" ? level : "info";
        var json = JsonSerializer.Serialize(new { enabled, level },
            new JsonSerializerOptions { WriteIndented = true });
        System.IO.File.WriteAllText(InAppTracePath, json);

        // Apply immediately to the running service
        _trace.Enabled = enabled;
        _trace.Level    = level;
        if (!enabled) _trace.Clear();

        _logger.LogInformation("Settings: In-App Trace {State} — level={Level}", enabled ? "enabled" : "disabled", level);
        TempData["TraceSaved"] = "true";
        TraceLogger.Exit("Settings", nameof(SaveInAppTrace));
        return RedirectToAction(nameof(Index));
    }

    [SkipSessionAuth]
    [HttpGet("Settings/TraceLatest")]
    public IActionResult TraceLatest(int count = 2)
    {
        // NOTE: No TraceLogger calls here — this endpoint is polled every 2s.
        // Adding Enter/Exit would flood the ring buffer with self-referential
        // trace lines, pushing out the actual useful diagnostic data.
        var lines = _trace.GetRecent(count);
        return Ok(new { enabled = _trace.Enabled, level = _trace.Level, lines });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Highway model)
    {
        TraceLogger.Enter("Settings", nameof(Create));
        if (ModelState.IsValid)
        {
            model.CreatedDate = DateTime.UtcNow;
            _db.Highways.Add(model);
            await _db.SaveChangesAsync();
        }
        TraceLogger.Exit("Settings", nameof(Create));
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Highway model)
    {
        TraceLogger.Enter("Settings", nameof(Edit));
        if (ModelState.IsValid)
        {
            _db.Highways.Update(model);
            await _db.SaveChangesAsync();
        }
        TraceLogger.Exit("Settings", nameof(Edit));
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        TraceLogger.Enter("Settings", nameof(Delete));
        var h = await _db.Highways.FindAsync(id);
        if (h != null) { _db.Highways.Remove(h); await _db.SaveChangesAsync(); }
        TraceLogger.Exit("Settings", nameof(Delete));
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult SavePurgeSettings(int purgeMaxDays, int purgeMaxCount)
    {
        TraceLogger.Enter("Settings", nameof(SavePurgeSettings));
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
        TraceLogger.Exit("Settings", nameof(SavePurgeSettings));
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RunPurge()
    {
        TraceLogger.Enter("Settings", nameof(RunPurge));
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
        TraceLogger.Exit("Settings", nameof(RunPurge));
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
        TraceLogger.Enter("Settings", nameof(SaveTomTomKey));
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
        TraceLogger.Exit("Settings", nameof(SaveTomTomKey));
        return RedirectToAction(nameof(Index));
    }
}