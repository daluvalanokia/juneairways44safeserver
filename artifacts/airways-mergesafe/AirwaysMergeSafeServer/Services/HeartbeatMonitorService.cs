using AirwaysMergeSafeServer.Data;
using AirwaysMergeSafeServer.Infrastructure;
using AirwaysMergeSafeServer.Models;
using Microsoft.EntityFrameworkCore;

namespace AirwaysMergeSafeServer.Services;

/// <summary>
/// D6 / E5 FIX: Background service that periodically scans SensorDevices
///              and SwitchServers. If LastHeartbeat is older than the configured
///              timeout, the record's Status is automatically set to "offline"
///              so the dashboard reflects real device state.
///
/// Configuration (appsettings.json):
///   "HeartbeatMonitor": {
///     "SensorTimeoutMinutes":  5,   // mark sensor offline after 5 min silence
///     "ServerTimeoutMinutes": 10,   // mark switch-server offline after 10 min
///     "PollIntervalMinutes":   2    // how often to run the check
///   }
/// </summary>
public class HeartbeatMonitorService : BackgroundService
{
    private readonly IServiceScopeFactory          _scopeFactory;
    private readonly ILogger<HeartbeatMonitorService> _logger;
    private readonly IConfiguration                _cfg;

    public HeartbeatMonitorService(
        IServiceScopeFactory             scopeFactory,
        ILogger<HeartbeatMonitorService> logger,
        IConfiguration                   cfg)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;
        _cfg          = cfg;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TraceLogger.Enter("HeartbeatMonitorService", nameof(ExecuteAsync));
        var sensorTimeout = TimeSpan.FromMinutes(
            _cfg.GetValue<double>("HeartbeatMonitor:SensorTimeoutMinutes", 5));
        var serverTimeout = TimeSpan.FromMinutes(
            _cfg.GetValue<double>("HeartbeatMonitor:ServerTimeoutMinutes", 10));
        var pollInterval  = TimeSpan.FromMinutes(
            _cfg.GetValue<double>("HeartbeatMonitor:PollIntervalMinutes",  2));

        _logger.LogInformation(
            "HeartbeatMonitor started — sensor timeout {S} min, server timeout {V} min, poll {P} min",
            sensorTimeout.TotalMinutes, serverTimeout.TotalMinutes, pollInterval.TotalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunCheckAsync(sensorTimeout, serverTimeout, stoppingToken);
            }
            catch (OperationCanceledException) { TraceLogger.Info("HeartbeatMonitorService", nameof(ExecuteAsync), "Cancelled — stopping loop"); break; }
            catch (Exception ex)
            {
                TraceLogger.Error("HeartbeatMonitorService", nameof(ExecuteAsync), ex);
                _logger.LogError(ex, "HeartbeatMonitor: unhandled error during check cycle");
            }

            await Task.Delay(pollInterval, stoppingToken);
        }

        TraceLogger.Exit("HeartbeatMonitorService", nameof(ExecuteAsync), "stopped");
        _logger.LogInformation("HeartbeatMonitor stopped.");
    }

    private async Task RunCheckAsync(
        TimeSpan sensorTimeout,
        TimeSpan serverTimeout,
        CancellationToken ct)
    {
        TraceLogger.Enter("HeartbeatMonitorService", nameof(RunCheckAsync));
        try
        {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db  = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;

        // ── Sensors ───────────────────────────────────────────────────────
        var staleSensors = await db.SensorDevices
            .Where(d => d.Status != "offline" && d.Status != "maintenance"
                     && d.LastHeartbeat < now - sensorTimeout)
            .ToListAsync(ct);

        if (staleSensors.Count > 0)
        {
            foreach (var s in staleSensors)
            {
                _logger.LogWarning(
                    "HeartbeatMonitor: Sensor {DeviceId} marked offline — last heartbeat {LastHB}",
                    s.DeviceId, s.LastHeartbeat);
                s.Status = "offline";
            }
            await db.SaveChangesAsync(ct);
        }

        // ── Switch Servers ────────────────────────────────────────────────
        var staleServers = await db.SwitchServers
            .Where(s => s.Status != "offline"
                     && s.LastHeartbeat < now - serverTimeout)
            .ToListAsync(ct);

        if (staleServers.Count > 0)
        {
            foreach (var s in staleServers)
            {
                _logger.LogWarning(
                    "HeartbeatMonitor: SwitchServer {ServerId} marked offline — last heartbeat {LastHB}",
                    s.ServerId, s.LastHeartbeat);
                s.Status = "offline";
            }
            await db.SaveChangesAsync(ct);
        }

        if (staleSensors.Count > 0 || staleServers.Count > 0)
            _logger.LogInformation(
                "HeartbeatMonitor: cycle complete — {SC} sensor(s), {SV} server(s) marked offline",
                staleSensors.Count, staleServers.Count);
        TraceLogger.Exit("HeartbeatMonitorService", nameof(RunCheckAsync),
            $"sensors={staleSensors.Count}, servers={staleServers.Count}");
        }
        catch (Exception ex) { TraceLogger.Error("HeartbeatMonitorService", nameof(RunCheckAsync), ex); throw; }
    }
}
