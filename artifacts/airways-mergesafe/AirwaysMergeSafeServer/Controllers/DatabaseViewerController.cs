using AirwaysMergeSafeServer.Data;
using AirwaysMergeSafeServer.Filters;
using AirwaysMergeSafeServer.Models;
using AirwaysMergeSafeServer.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AirwaysMergeSafeServer.Controllers;

/// <summary>
/// A6 FIX: UserProfile.Password hash masked as "***" in viewer output.
/// DEV FIX: Delete uses POST + PRG (Post/Redirect/Get) to fix browser refresh
///           re-submitting the DELETE action on F5. TempData carries the result
///           message so it survives the redirect without being lost.
///          ResetAndReseed: drops and re-seeds the entire DB (dev only).
/// </summary>
[AdminOnly]
public class DatabaseViewerController : Controller
{
    private static readonly HashSet<string> SensitiveColumns =
        new(StringComparer.OrdinalIgnoreCase) { "Password", "DeviceApiKey", "TomTomApiKey" };

    private readonly AppDbContext _db;
    private readonly ILogger<DatabaseViewerController> _logger;
    private const int PageSize = 50;

    public DatabaseViewerController(AppDbContext db, ILogger<DatabaseViewerController> logger)
    { _db = db; _logger = logger; }

    // ── GET: /DatabaseViewer ──────────────────────────────────────────────
    public async Task<IActionResult> Index(string? table, int page = 1)
    {
        var userId = HttpContext.Session.GetString("UserId") ?? "unknown";
        _logger.LogInformation(
            "Security: Admin action — DatabaseViewer accessed userId={UserId} table={Table}",
            userId, table ?? "Highways");

        table ??= "Highways";
        page   = Math.Max(1, page);

        var summary = await BuildSummaryAsync();
        var (total, columns, rows) = await LoadTableAsync(table, page);

        var vm = new DatabaseViewerViewModel
        {
            SelectedTable = table, Page = page, PageSize = PageSize,
            TotalRows = total, Columns = columns, Rows = rows, TableSummary = summary
        };

        // PRG: surface success/error messages set by Delete / ResetAndReseed
        if (TempData["SuccessMessage"] is string success) ViewBag.SuccessMessage = success;
        if (TempData["ErrorMessage"]   is string error)   ViewBag.ErrorMessage   = error;

        return View(vm);
    }

    // ── POST: /DatabaseViewer/DeleteRow ──────────────────────────────────
    // PRG pattern — POST deletes, then redirects to GET (prevents F5 re-submit)
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteRow(string table, int id, int page = 1)
    {
        var userId = HttpContext.Session.GetString("UserId") ?? "unknown";
        _logger.LogWarning(
            "Security: Admin DELETE — userId={UserId} table={Table} id={Id}", userId, table, id);

        try
        {
            int deleted = table switch
            {
                "Highways"             => await DeleteEntity2(_db.Highways,             id),
                "MergeZones"           => await DeleteEntity2(_db.MergeZones,           id),
                "SwitchServers"        => await DeleteEntity2(_db.SwitchServers,        id),
                "SensorDevices"        => await DeleteEntity2(_db.SensorDevices,        id),
                "TriangulationConfigs" => await DeleteEntity2(_db.TriangulationConfigs, id),
                "VehicleEvents"        => await DeleteEntity2(_db.VehicleEvents,        id),
                "InputFormatConfigs"   => await DeleteEntity2(_db.InputFormatConfigs,   id),
                "SamplePayloads"       => await DeleteEntity2(_db.SamplePayloads,       id),
                "UserProfiles"         => await DeleteEntity2(_db.UserProfiles,         id),
                _ => 0
            };

            if (deleted > 0)
                TempData["SuccessMessage"] = $"Row id={id} deleted from {table}.";
            else
                TempData["ErrorMessage"] = $"Row id={id} not found in {table}.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DatabaseViewer: DeleteRow failed table={Table} id={Id}", table, id);
            TempData["ErrorMessage"] = $"Delete failed: {ex.Message}";
        }

        // PRG redirect — prevents browser F5 from re-POSTing the delete
        return RedirectToAction(nameof(Index), new { table, page });
    }

    // ── POST: /DatabaseViewer/ResetAndReseed ─────────────────────────────
    // Dev-only: wipes all tables and re-runs the seed.
    // Guarded by environment check — will not execute in Production.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetAndReseed()
    {
        var userId = HttpContext.Session.GetString("UserId") ?? "unknown";
        var env    = HttpContext.RequestServices
                        .GetRequiredService<IWebHostEnvironment>();

        if (!env.IsDevelopment())
        {
            _logger.LogWarning(
                "Security: ResetAndReseed blocked — not Development. userId={UserId}", userId);
            TempData["ErrorMessage"] = "ResetAndReseed is only available in Development.";
            return RedirectToAction(nameof(Index));
        }

        _logger.LogWarning(
            "Security: Admin ResetAndReseed — userId={UserId} wiping all tables", userId);

        try
        {
            // Wipe in FK-safe order (children before parents)
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM AuditLogs");
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM VehicleEvents");
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM SamplePayloads");
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM InputFormatConfigs");
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM TriangulationConfigs");
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM SensorDevices");
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM SwitchServers");
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM MergeZones");
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM UserProfiles");
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM Highways");

            // Reset SQLite auto-increment counters so IDs start from 1 again
            var tables = new[]
            {
                "AuditLogs","VehicleEvents","SamplePayloads","InputFormatConfigs",
                "TriangulationConfigs","SensorDevices","SwitchServers",
                "MergeZones","UserProfiles","Highways"
            };
            foreach (var t in tables)
            {
                try
                {
                    await _db.Database.ExecuteSqlRawAsync(
                        $"DELETE FROM sqlite_sequence WHERE name='{t}'");
                }
                catch { /* sqlite_sequence may not exist if table was never inserted */ }
            }

            // Re-seed
            DbInitializer.Seed(_db);

            TempData["SuccessMessage"] = "Database reset and re-seeded successfully.";
            _logger.LogInformation("DatabaseViewer: ResetAndReseed completed — userId={UserId}", userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DatabaseViewer: ResetAndReseed failed");
            TempData["ErrorMessage"] = $"Reset failed: {ex.Message}";
        }

        return RedirectToAction(nameof(Index));
    }

    // ── Private helpers ───────────────────────────────────────────────────
private async Task<int> DeleteEntity2<T>(DbSet<T> set, int id)
        where T : class
    {
        var entity = await set.FindAsync(id);
        if (entity is null) return 0;
        set.Remove(entity);
        return await _db.SaveChangesAsync();
    }

    private async Task<List<(string Name, int Count)>> BuildSummaryAsync() => new()
    {
        ("Highways",             await _db.Highways.AsNoTracking().CountAsync()),
        ("MergeZones",           await _db.MergeZones.AsNoTracking().CountAsync()),
        ("SwitchServers",        await _db.SwitchServers.AsNoTracking().CountAsync()),
        ("SensorDevices",        await _db.SensorDevices.AsNoTracking().CountAsync()),
        ("TriangulationConfigs", await _db.TriangulationConfigs.AsNoTracking().CountAsync()),
        ("VehicleEvents",        await _db.VehicleEvents.AsNoTracking().CountAsync()),
        ("InputFormatConfigs",   await _db.InputFormatConfigs.AsNoTracking().CountAsync()),
        ("SamplePayloads",       await _db.SamplePayloads.AsNoTracking().CountAsync()),
        ("UserProfiles",         await _db.UserProfiles.AsNoTracking().CountAsync()),
    };

    private async Task<(int, List<string>, List<Dictionary<string,string>>)>
        LoadTableAsync(string table, int page)
    {
        int skip = (page - 1) * PageSize;
        return table switch
        {
            "Highways"             => (await _db.Highways.CountAsync(), Cols<Highway>(),
                Mask(ToRows(await _db.Highways.AsNoTracking().OrderBy(x => x.Id).Skip(skip).Take(PageSize).ToListAsync()))),
            "MergeZones"           => (await _db.MergeZones.CountAsync(), Cols<MergeZone>(),
                Mask(ToRows(await _db.MergeZones.AsNoTracking().OrderBy(x => x.Id).Skip(skip).Take(PageSize).ToListAsync()))),
            "SwitchServers"        => (await _db.SwitchServers.CountAsync(), Cols<SwitchServer>(),
                Mask(ToRows(await _db.SwitchServers.AsNoTracking().OrderBy(x => x.Id).Skip(skip).Take(PageSize).ToListAsync()))),
            "SensorDevices"        => (await _db.SensorDevices.CountAsync(), Cols<SensorDevice>(),
                Mask(ToRows(await _db.SensorDevices.AsNoTracking().OrderBy(x => x.Id).Skip(skip).Take(PageSize).ToListAsync()))),
            "TriangulationConfigs" => (await _db.TriangulationConfigs.CountAsync(), Cols<TriangulationConfig>(),
                Mask(ToRows(await _db.TriangulationConfigs.AsNoTracking().OrderBy(x => x.Id).Skip(skip).Take(PageSize).ToListAsync()))),
            "VehicleEvents"        => (await _db.VehicleEvents.CountAsync(), Cols<VehicleEvent>(),
                Mask(ToRows(await _db.VehicleEvents.AsNoTracking().OrderByDescending(x => x.CreatedDate).Skip(skip).Take(PageSize).ToListAsync()))),
            "InputFormatConfigs"   => (await _db.InputFormatConfigs.CountAsync(), Cols<InputFormatConfig>(),
                Mask(ToRows(await _db.InputFormatConfigs.AsNoTracking().OrderBy(x => x.Id).Skip(skip).Take(PageSize).ToListAsync()))),
            "SamplePayloads"       => (await _db.SamplePayloads.CountAsync(), Cols<SamplePayload>(),
                Mask(ToRows(await _db.SamplePayloads.AsNoTracking().OrderByDescending(x => x.CreatedDate).Skip(skip).Take(PageSize).ToListAsync()))),
            "UserProfiles"         => (await _db.UserProfiles.CountAsync(), Cols<UserProfile>(),
                Mask(ToRows(await _db.UserProfiles.AsNoTracking().OrderBy(x => x.Id).Skip(skip).Take(PageSize).ToListAsync()))),
            _ => (0, new(), new())
        };
    }

    // A6 FIX: mask sensitive columns
    private static List<Dictionary<string,string>> Mask(List<Dictionary<string,string>> rows)
    {
        foreach (var row in rows)
            foreach (var key in row.Keys.Where(k => SensitiveColumns.Contains(k)).ToList())
                if (!string.IsNullOrEmpty(row[key])) row[key] = "***";
        return rows;
    }

    private static List<string> Cols<T>() =>
        typeof(T).GetProperties().Select(p => p.Name).ToList();

    private static readonly JsonSerializerOptions _opts = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        NumberHandling = JsonNumberHandling.WriteAsString
    };

    private static List<Dictionary<string,string>> ToRows<T>(IEnumerable<T> items)
    {
        var json = JsonSerializer.Serialize(items.Cast<object>(), _opts);
        var raw  = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(json) ?? new();
        return raw.Select(r => r.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.ValueKind == JsonValueKind.Null ? "" : kv.Value.ToString()
        )).ToList();
    }
}
