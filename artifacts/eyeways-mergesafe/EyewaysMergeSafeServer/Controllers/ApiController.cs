using EyewaysMergeSafeServer.Data;
using EyewaysMergeSafeServer.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;

namespace EyewaysMergeSafeServer.Controllers;

[Route("api")]
[ApiController]
public class ApiController : ControllerBase
{
    private readonly AppDbContext _db;
    public ApiController(AppDbContext db) { _db = db; }

    // ── Read endpoints (GET — public, no auth required) ────────────────────

    [HttpGet("stats"), OutputCache(PolicyName = "ShortLive")]
    public async Task<IActionResult> Stats(string? highwayId)
    {
        var zones   = await _db.MergeZones.AsNoTracking().Where(z => highwayId == null || z.HighwayId == highwayId).CountAsync();
        var servers = await _db.SwitchServers.AsNoTracking().Where(s => highwayId == null || s.HighwayId == highwayId).CountAsync();
        var sensors = await _db.SensorDevices.AsNoTracking().Where(d => highwayId == null || d.HighwayId == highwayId).CountAsync();
        var events  = await _db.VehicleEvents.AsNoTracking().Where(e => highwayId == null || e.HighwayId == highwayId).CountAsync();
        return Ok(new { zones, servers, sensors, events, timestamp = DateTime.UtcNow });
    }

    [HttpGet("zone/{zoneId}/status")]
    public async Task<IActionResult> ZoneStatus(string zoneId)
    {
        var zone = await _db.MergeZones.AsNoTracking().FirstOrDefaultAsync(z => z.ZoneId == zoneId);
        if (zone == null) return NotFound();
        var sensors = await _db.SensorDevices.AsNoTracking().Where(d => d.ZoneId == zoneId).ToListAsync();
        var servers = await _db.SwitchServers.AsNoTracking().Where(s => s.ZoneId == zoneId).ToListAsync();
        return Ok(new
        {
            zoneId,
            zoneName = zone.ZoneName,
            status   = zone.Status,
            sensors  = sensors.Select(s => new { s.DeviceId, s.DeviceName, s.Status }),
            servers  = servers.Select(s => new { s.ServerId, s.ServerName, s.Status })
        });
    }

    /// <summary>
    /// Recent vehicle events for live feed polling on Traffic3D/Traffic pages.
    /// Returns events created after <paramref name="since"/> (ISO-8601 UTC).
    /// Defaults to events from the last 30 seconds when <paramref name="since"/> is omitted.
    /// </summary>
    [HttpGet("events/live")]
    public async Task<IActionResult> LiveEvents(string? highwayId, string? zoneId, string? since)
    {
        DateTime cutoff;
        if (string.IsNullOrEmpty(since) ||
            !DateTime.TryParse(since, null, System.Globalization.DateTimeStyles.RoundtripKind, out cutoff))
        {
            cutoff = DateTime.UtcNow.AddSeconds(-30);
        }

        var events = await _db.VehicleEvents
            .AsNoTracking()
            .Where(e => e.CreatedDate >= cutoff &&
                        (string.IsNullOrEmpty(highwayId) || e.HighwayId == highwayId) &&
                        (string.IsNullOrEmpty(zoneId)    || e.ZoneId    == zoneId))
            .OrderByDescending(e => e.CreatedDate)
            .Take(20)
            .ToListAsync();

        return Ok(new
        {
            events = events.Select(e => new
            {
                e.EventType,
                e.VehicleId,
                e.SpeedMph,
                e.ZoneId,
                vehicleType  = ParsePayload(e.Payload, "vehicle_type"),
                vehicleColor = ParsePayload(e.Payload, "color"),
                vehicleMake  = ParsePayload(e.Payload, "make"),
                vehicleModel = ParsePayload(e.Payload, "model"),
                e.CreatedDate
            }),
            serverTime = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Simulation-only vehicle events (VehicleId starts with "SIM-").
    /// Used by Traffic3D when paused — shows only simulator-generated data.
    /// </summary>
    [HttpGet("events/simulation")]
    public async Task<IActionResult> SimulationEvents(string? highwayId, string? zoneId, string? since)
    {
        DateTime cutoff;
        if (string.IsNullOrEmpty(since) ||
            !DateTime.TryParse(since, null, System.Globalization.DateTimeStyles.RoundtripKind, out cutoff))
        {
            cutoff = DateTime.UtcNow.AddSeconds(-30);
        }

        var events = await _db.VehicleEvents
            .AsNoTracking()
            .Where(e => e.CreatedDate >= cutoff &&
                        e.VehicleId != null && e.VehicleId.StartsWith("SIM-") &&
                        (string.IsNullOrEmpty(highwayId) || e.HighwayId == highwayId) &&
                        (string.IsNullOrEmpty(zoneId)    || e.ZoneId    == zoneId))
            .OrderByDescending(e => e.CreatedDate)
            .Take(20)
            .ToListAsync();

        return Ok(new
        {
            events = events.Select(e => new
            {
                e.EventType,
                e.VehicleId,
                e.SpeedMph,
                e.ZoneId,
                vehicleType  = ParsePayload(e.Payload, "vehicle_type"),
                vehicleColor = ParsePayload(e.Payload, "color"),
                vehicleMake  = ParsePayload(e.Payload, "make"),
                vehicleModel = ParsePayload(e.Payload, "model"),
                e.CreatedDate
            }),
            serverTime = DateTime.UtcNow
        });
    }

    // ── Write endpoints (POST — require session OR X-Device-Token) ──────────

    /// <summary>
    /// Ingest a vehicle event. Requires valid session or X-Device-Token header.
    /// </summary>
    [HttpPost("events/ingest")]
    public async Task<IActionResult> IngestEvent([FromBody] VehicleEvent ev)
    {
        ev.CreatedDate = DateTime.UtcNow;
        _db.VehicleEvents.Add(ev);
        await _db.SaveChangesAsync();
        return Ok(new { id = ev.Id, status = "accepted", timestamp = ev.CreatedDate });
    }

    /// <summary>
    /// Sensor device heartbeat. Requires valid session or X-Device-Token header.
    /// </summary>
    [HttpPost("sensor/{deviceId}/heartbeat")]
    public async Task<IActionResult> SensorHeartbeat(string deviceId)
    {
        var sensor = await _db.SensorDevices.FirstOrDefaultAsync(d => d.DeviceId == deviceId);
        if (sensor == null) return NotFound(new { deviceId, error = "device not found" });
        sensor.LastHeartbeat = DateTime.UtcNow;
        sensor.Status = "online";
        await _db.SaveChangesAsync();
        return Ok(new { deviceId, status = "online", timestamp = DateTime.UtcNow });
    }

    /// <summary>
    /// Switch server heartbeat. Requires valid session or X-Device-Token header.
    /// </summary>
    [HttpPost("server/{serverId}/heartbeat")]
    public async Task<IActionResult> ServerHeartbeat(string serverId)
    {
        var server = await _db.SwitchServers.FirstOrDefaultAsync(s => s.ServerId == serverId);
        if (server == null) return NotFound(new { serverId, error = "server not found" });
        server.LastHeartbeat = DateTime.UtcNow;
        server.Status = "online";
        await _db.SaveChangesAsync();
        return Ok(new { serverId, status = "online", timestamp = DateTime.UtcNow });
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static string? ParsePayload(string? payload, string field)
    {
        if (string.IsNullOrEmpty(payload)) return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(payload);
            return doc.RootElement.TryGetProperty(field, out var v) ? v.GetString() : null;
        }
        catch { return null; }
    }
}
