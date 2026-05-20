using EyewaysMergeSafeServer.Data;
using EyewaysMergeSafeServer.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EyewaysMergeSafeServer.Controllers;

[Route("api")]
[ApiController]
public class ApiController : ControllerBase
{
    private readonly AppDbContext _db;
    public ApiController(AppDbContext db) { _db = db; }

    [HttpGet("stats")]
    public async Task<IActionResult> Stats(string? highwayId)
    {
        var zones   = await _db.MergeZones.AsNoTracking().Where(z => highwayId == null || z.HighwayId == highwayId).CountAsync();
        var servers = await _db.SwitchServers.AsNoTracking().Where(s => highwayId == null || s.HighwayId == highwayId).CountAsync();
        var sensors = await _db.SensorDevices.AsNoTracking().Where(d => highwayId == null || d.HighwayId == highwayId).CountAsync();
        var events  = await _db.VehicleEvents.AsNoTracking().Where(e => highwayId == null || e.HighwayId == highwayId).CountAsync();
        return Ok(new { zones, servers, sensors, events, timestamp = DateTime.UtcNow });
    }

    [HttpPost("events/ingest")]
    public async Task<IActionResult> IngestEvent([FromBody] VehicleEvent ev)
    {
        ev.CreatedDate = DateTime.UtcNow;
        _db.VehicleEvents.Add(ev);
        await _db.SaveChangesAsync();
        return Ok(new { id = ev.Id, status = "accepted" });
    }

    [HttpPost("sensor/{deviceId}/heartbeat")]
    public async Task<IActionResult> SensorHeartbeat(string deviceId)
    {
        var sensor = await _db.SensorDevices.FirstOrDefaultAsync(d => d.DeviceId == deviceId);
        if (sensor == null) return NotFound();
        sensor.LastHeartbeat = DateTime.UtcNow;
        sensor.Status = "online";
        await _db.SaveChangesAsync();
        return Ok(new { deviceId, status = "online", timestamp = DateTime.UtcNow });
    }

    [HttpPost("server/{serverId}/heartbeat")]
    public async Task<IActionResult> ServerHeartbeat(string serverId)
    {
        var server = await _db.SwitchServers.FirstOrDefaultAsync(s => s.ServerId == serverId);
        if (server == null) return NotFound();
        server.LastHeartbeat = DateTime.UtcNow;
        server.Status = "online";
        await _db.SaveChangesAsync();
        return Ok(new { serverId, status = "online", timestamp = DateTime.UtcNow });
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
            status = zone.Status,
            sensors = sensors.Select(s => new { s.DeviceId, s.DeviceName, s.Status }),
            servers = servers.Select(s => new { s.ServerId, s.ServerName, s.Status })
        });
    }
}
