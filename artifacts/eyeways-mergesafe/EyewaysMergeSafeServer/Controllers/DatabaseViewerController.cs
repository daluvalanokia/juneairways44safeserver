using EyewaysMergeSafeServer.Data;
using EyewaysMergeSafeServer.Filters;
using EyewaysMergeSafeServer.Models;
using EyewaysMergeSafeServer.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EyewaysMergeSafeServer.Controllers;

[AdminOnly]
public class DatabaseViewerController : Controller
{
    private readonly AppDbContext _db;
    private readonly ILogger<DatabaseViewerController> _logger;
    private const int PageSize = 50;

    public DatabaseViewerController(AppDbContext db, ILogger<DatabaseViewerController> logger)
    {
        _db     = db;
        _logger = logger;
    }

    public async Task<IActionResult> Index(string? table, int page = 1)
    {
        var userId = HttpContext.Session.GetString("UserId") ?? "unknown";
        _logger.LogInformation("Security: Admin action — DatabaseViewer accessed userId={UserId} table={Table}",
            userId, table ?? "Highways");

        table ??= "Highways";
        page   = Math.Max(1, page);

        var summary = await BuildSummaryAsync();
        var (total, columns, rows) = await LoadTableAsync(table, page);

        return View(new DatabaseViewerViewModel
        {
            SelectedTable = table,
            Page          = page,
            PageSize      = PageSize,
            TotalRows     = total,
            Columns       = columns,
            Rows          = rows,
            TableSummary  = summary
        });
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

    private async Task<(int total, List<string> cols, List<Dictionary<string,string>> rows)>
        LoadTableAsync(string table, int page)
    {
        int skip = (page - 1) * PageSize;
        return table switch
        {
            "Highways" => (
                await _db.Highways.AsNoTracking().CountAsync(),
                Cols<Highway>(),
                ToRows(await _db.Highways.AsNoTracking().OrderBy(x => x.Id).Skip(skip).Take(PageSize).ToListAsync())),

            "MergeZones" => (
                await _db.MergeZones.AsNoTracking().CountAsync(),
                Cols<MergeZone>(),
                ToRows(await _db.MergeZones.AsNoTracking().OrderBy(x => x.Id).Skip(skip).Take(PageSize).ToListAsync())),

            "SwitchServers" => (
                await _db.SwitchServers.AsNoTracking().CountAsync(),
                Cols<SwitchServer>(),
                ToRows(await _db.SwitchServers.AsNoTracking().OrderBy(x => x.Id).Skip(skip).Take(PageSize).ToListAsync())),

            "SensorDevices" => (
                await _db.SensorDevices.AsNoTracking().CountAsync(),
                Cols<SensorDevice>(),
                ToRows(await _db.SensorDevices.AsNoTracking().OrderBy(x => x.Id).Skip(skip).Take(PageSize).ToListAsync())),

            "TriangulationConfigs" => (
                await _db.TriangulationConfigs.AsNoTracking().CountAsync(),
                Cols<TriangulationConfig>(),
                ToRows(await _db.TriangulationConfigs.AsNoTracking().OrderBy(x => x.Id).Skip(skip).Take(PageSize).ToListAsync())),

            "VehicleEvents" => (
                await _db.VehicleEvents.AsNoTracking().CountAsync(),
                Cols<VehicleEvent>(),
                ToRows(await _db.VehicleEvents.AsNoTracking().OrderByDescending(x => x.CreatedDate).Skip(skip).Take(PageSize).ToListAsync())),

            "InputFormatConfigs" => (
                await _db.InputFormatConfigs.AsNoTracking().CountAsync(),
                Cols<InputFormatConfig>(),
                ToRows(await _db.InputFormatConfigs.AsNoTracking().OrderBy(x => x.Id).Skip(skip).Take(PageSize).ToListAsync())),

            "SamplePayloads" => (
                await _db.SamplePayloads.AsNoTracking().CountAsync(),
                Cols<SamplePayload>(),
                ToRows(await _db.SamplePayloads.AsNoTracking().OrderByDescending(x => x.CreatedDate).Skip(skip).Take(PageSize).ToListAsync())),

            "UserProfiles" => (
                await _db.UserProfiles.AsNoTracking().CountAsync(),
                Cols<UserProfile>(),
                ToRows(await _db.UserProfiles.AsNoTracking().OrderBy(x => x.Id).Skip(skip).Take(PageSize).ToListAsync())),

            _ => (0, new(), new())
        };
    }

    private static List<string> Cols<T>() =>
        typeof(T).GetProperties().Select(p => p.Name).ToList();

    private static readonly JsonSerializerOptions _opts = new()
    {
        WriteIndented            = false,
        DefaultIgnoreCondition   = JsonIgnoreCondition.Never,
        NumberHandling           = JsonNumberHandling.WriteAsString
    };

    private static List<Dictionary<string, string>> ToRows<T>(IEnumerable<T> items)
    {
        var json = JsonSerializer.Serialize(items.Cast<object>(), _opts);
        var raw  = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(json) ?? new();
        return raw.Select(r => r.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.ValueKind == JsonValueKind.Null ? "" : kv.Value.ToString()
        )).ToList();
    }
}
