using AirwaysMergeSafeServer.Data;
using AirwaysMergeSafeServer.Filters;
using AirwaysMergeSafeServer.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AirwaysMergeSafeServer.Controllers;

/// <summary>
/// E4: Health check endpoint — GET /health
///     Returns 200 OK with a structured JSON report when all checks pass,
///     503 Service Unavailable if any critical check fails.
///     Exempt from SessionAuthFilter so load balancers / uptime monitors
///     can reach it without a session cookie.
///
/// Checks performed:
///   • Database ping (SELECT 1)
///   • Entity record counts (Highways, Sensors, Servers)
///   • HeartbeatMonitorService running status
///   • Runtime environment info
/// </summary>
[SkipSessionAuth]   // custom attribute — bypasses global SessionAuthFilter
public class HealthController : Controller
{
    private readonly AppDbContext                 _db;
    private readonly ILogger<HealthController>    _logger;

    public HealthController(AppDbContext db, ILogger<HealthController> logger)
    { _db = db; _logger = logger; }

    [HttpGet("/health")]
    public async Task<IActionResult> Health()
    {
        var checks = new Dictionary<string, object>();
        var overallOk = true;

        // ── DB ping ────────────────────────────────────────────────────────
        try
        {
            await _db.Database.ExecuteSqlRawAsync("SELECT 1");
            checks["database"] = new { status = "ok" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health: DB ping failed");
            checks["database"] = new { status = "fail", error = ex.Message };
            overallOk = false;
        }

        // ── Entity counts ──────────────────────────────────────────────────
        try
        {
            checks["entities"] = new
            {
                status    = "ok",
                highways  = await _db.Highways.AsNoTracking().CountAsync(),
                sensors   = await _db.SensorDevices.AsNoTracking().CountAsync(),
                servers   = await _db.SwitchServers.AsNoTracking().CountAsync(),
                zones     = await _db.MergeZones.AsNoTracking().CountAsync(),
                events    = await _db.VehicleEvents.AsNoTracking().CountAsync(),
                auditLogs = await _db.AuditLogs.AsNoTracking().CountAsync()
            };
        }
        catch (Exception ex)
        {
            checks["entities"] = new { status = "fail", error = ex.Message };
            overallOk = false;
        }

        // ── Runtime info ───────────────────────────────────────────────────
        checks["runtime"] = new
        {
            status      = "ok",
            environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production",
            utcNow      = DateTime.UtcNow.ToString("o"),
            dotnetVersion = Environment.Version.ToString(),
            uptime      = (DateTime.UtcNow - System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime()).ToString(@"d\.hh\:mm\:ss")
        };

        // ── Response ───────────────────────────────────────────────────────
        var result = new
        {
            status  = overallOk ? "healthy" : "degraded",
            version = "1.0.0",
            checks
        };

        return overallOk
            ? Ok(result)
            : StatusCode(StatusCodes.Status503ServiceUnavailable, result);
    }
}
