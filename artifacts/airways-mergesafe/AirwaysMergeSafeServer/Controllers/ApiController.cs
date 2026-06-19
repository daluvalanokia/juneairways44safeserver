using AirwaysMergeSafeServer.Data;
using AirwaysMergeSafeServer.Models;
using AirwaysMergeSafeServer.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace AirwaysMergeSafeServer.Controllers;

/// <summary>
/// Phase 6: /api/events/live now projects VehicleMode + VehicleCategory + VehicleClassJson
///          so the 3D scene JS can render the correct geometry without re-classifying.
///          /api/events/ingest now classifies on arrival and stores result.
/// </summary>
[Route("api")]
[ApiController]
public class ApiController : ControllerBase
{
    private static readonly HashSet<string> AllowedEventTypes =
        new(StringComparer.OrdinalIgnoreCase)
        { "detection", "merge", "conflict", "speeding", "fault" };

    private readonly AppDbContext      _db;
    private readonly IConfiguration    _cfg;
    private readonly VehicleClassifier _classifier;
    private readonly ILogger<ApiController> _logger;

    public ApiController(AppDbContext db, IConfiguration cfg,
        VehicleClassifier classifier, ILogger<ApiController> logger)
    { _db = db; _cfg = cfg; _classifier = classifier; _logger = logger; }

    private bool IsAuthorised()
    {
        if (HttpContext.Session.GetString("UserId") is { Length: > 0 }) return true;
        var configKey = _cfg["DeviceApiKey"];
        if (string.IsNullOrEmpty(configKey)) return false;
        var headerKey = HttpContext.Request.Headers["X-Device-Api-Key"].FirstOrDefault();
        return !string.IsNullOrEmpty(headerKey) && CryptographicEquals(headerKey, configKey);
    }

    private static bool CryptographicEquals(string a, string b)
    {
        if (a.Length != b.Length) return false;
        int diff = 0;
        for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
        return diff == 0;
    }

    [HttpGet("stats"), OutputCache(PolicyName = "ShortLive")]
    public async Task<IActionResult> Stats(string? highwayId)
    {
        if (!IsAuthorised()) return Unauthorized(new { error = "Authentication required." });
        var zones   = await _db.MergeZones.AsNoTracking().Where(z => highwayId == null || z.HighwayId == highwayId).CountAsync();
        var servers = await _db.SwitchServers.AsNoTracking().Where(s => highwayId == null || s.HighwayId == highwayId).CountAsync();
        var sensors = await _db.SensorDevices.AsNoTracking().Where(d => highwayId == null || d.HighwayId == highwayId).CountAsync();
        var events  = await _db.VehicleEvents.AsNoTracking().Where(e => highwayId == null || e.HighwayId == highwayId).CountAsync();
        var ground  = await _db.VehicleEvents.AsNoTracking().Where(e => (highwayId == null || e.HighwayId == highwayId) && e.VehicleMode == "ground").CountAsync();
        var air     = await _db.VehicleEvents.AsNoTracking().Where(e => (highwayId == null || e.HighwayId == highwayId) && e.VehicleMode == "air").CountAsync();
        return Ok(new { zones, servers, sensors, events, ground, air });
    }

    [HttpGet("zones"), OutputCache(PolicyName = "ShortLive")]
    public async Task<IActionResult> Zones(string? highwayId)
    {
        if (!IsAuthorised()) return Unauthorized(new { error = "Authentication required." });
        var zones = await _db.MergeZones.AsNoTracking()
            .Where(z => highwayId == null || z.HighwayId == highwayId)
            .OrderBy(z => z.HighwayId).ThenBy(z => z.ZoneName)
            .Select(z => new { z.Id, z.ZoneName, z.ZoneId, z.HighwayId,
                z.Status, z.MileMarker, z.Latitude, z.Longitude,
                z.GeofenceRadius, z.AltitudeMeters })
            .ToListAsync();
        return Ok(zones);
    }

    /// <summary>
    /// Phase 6: Live feed includes vehicleMode, vehicleCategory, vehicleClassJson.
    /// The 3D scene JS reads these to choose geometry + color without re-classifying.
    /// Optional filter: ?mode=ground|air&category=sedan|suv|…
    /// </summary>
    [HttpGet("events/live")]
    public async Task<IActionResult> EventsLive(
        string? highwayId, int take = 50,
        string? mode = null, string? category = null)
    {
        if (!IsAuthorised()) return Unauthorized(new { error = "Authentication required." });

        take      = Math.Clamp(take, 1, 200);
        highwayId ??= HttpContext.Session.GetString("HighwayId") ?? "";

        var q = _db.VehicleEvents.AsNoTracking().Where(e => e.HighwayId == highwayId);
        if (!string.IsNullOrEmpty(mode))     q = q.Where(e => e.VehicleMode     == mode);
        if (!string.IsNullOrEmpty(category)) q = q.Where(e => e.VehicleCategory == category);

        var events = await q.OrderByDescending(e => e.CreatedDate).Take(take)
            .Select(e => new {
                e.Id, e.EventType, e.ZoneId, e.HighwayId,
                e.VehicleId, e.SpeedMph,
                e.Latitude, e.Longitude, e.AltitudeMeters,
                // Phase 6
                e.VehicleMode, e.VehicleCategory, e.VehicleClassJson,
                e.CreatedDate
            })
            .ToListAsync();
        return Ok(events);
    }

    [HttpGet("altitudebands"), OutputCache(PolicyName = "ShortLive")]
    public async Task<IActionResult> AltitudeBands(string? highwayId)
    {
        if (!IsAuthorised()) return Unauthorized(new { error = "Authentication required." });
        var bands = await _db.SwitchServers.AsNoTracking()
            .Where(s => (highwayId == null || s.HighwayId == highwayId)
                     && s.AltitudeMinMeters.HasValue && s.AltitudeMaxMeters.HasValue)
            .Select(s => new { s.ServerId, s.ServerName, s.ZoneId, s.HighwayId,
                s.AltitudeMinMeters, s.AltitudeMaxMeters, s.AltitudeWidthMeters, s.Status })
            .ToListAsync();
        return Ok(bands);
    }

    /// <summary>Phase 6: Classify on ingest; store result in VehicleMode/Category/ClassJson.</summary>
    [HttpPost("events/ingest")]
    [RequestSizeLimit(32_768)]
    public async Task<IActionResult> IngestEvent([FromBody] IngestPayload payload)
    {
        if (!IsAuthorised()) return Unauthorized(new { error = "Authentication required." });
        if (payload is null) return BadRequest(new { error = "Payload required." });

        var eventType = payload.EventType?.Trim().ToLowerInvariant() ?? "detection";
        if (!AllowedEventTypes.Contains(eventType))
        {
            _logger.LogWarning("API: IngestEvent rejected — invalid EventType={EventType}", payload.EventType);
            return BadRequest(new { error = $"Invalid event_type. Allowed: {string.Join(", ", AllowedEventTypes)}" });
        }

        // Phase 6: Classify from ingest payload fields
        var syntheticJson = JsonSerializer.Serialize(new {
            altitude_m   = payload.AltitudeMeters,
            speed_mph    = payload.SpeedMph,
            vehicle_type = payload.VehicleType,
            latitude     = payload.Latitude,
            longitude    = payload.Longitude
        });
        var vc     = _classifier.Classify(syntheticJson, "physical");
        var vcJson = JsonSerializer.Serialize(vc, new JsonSerializerOptions
            { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        var ev = new VehicleEvent
        {
            EventType        = eventType,
            ZoneId           = Truncate(payload.ZoneId,    50),
            HighwayId        = Truncate(payload.HighwayId, 50) ?? "",
            DeviceId         = Truncate(payload.DeviceId,  50),
            VehicleId        = Truncate(payload.VehicleId, 50),
            SpeedMph         = payload.SpeedMph,
            Latitude         = payload.Latitude,
            Longitude        = payload.Longitude,
            AltitudeMeters   = payload.AltitudeMeters,
            VehicleMode      = vc.Domain,
            VehicleCategory  = vc.Category,
            VehicleClassJson = vcJson[..Math.Min(800, vcJson.Length)],
            Payload          = null,
            CreatedDate      = DateTime.UtcNow
        };

        _db.VehicleEvents.Add(ev);
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "API: IngestEvent — type={EventType} hw={HighwayId} mode={Mode} cat={Cat} confidence={Conf}",
            eventType, ev.HighwayId, vc.Domain, vc.Category, vc.Confidence);

        return Ok(new { ok = true, id = ev.Id,
            classification = new { domain = vc.Domain, category = vc.Category, confidence = vc.Confidence } });
    }

    private static string? Truncate(string? s, int max)
        => s is null ? null : s.Trim()[..Math.Min(max, s.Trim().Length)];

    public sealed class IngestPayload
    {
        public string? EventType      { get; set; }
        public string? ZoneId         { get; set; }
        public string? HighwayId      { get; set; }
        public string? DeviceId       { get; set; }
        public string? VehicleId      { get; set; }
        public string? VehicleType    { get; set; }  // Phase 6
        public double? SpeedMph       { get; set; }
        public double? Latitude       { get; set; }
        public double? Longitude      { get; set; }
        public double? AltitudeMeters { get; set; }
    }
}
