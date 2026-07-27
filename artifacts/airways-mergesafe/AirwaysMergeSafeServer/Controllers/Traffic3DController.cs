using AirwaysMergeSafeServer.Data;
using AirwaysMergeSafeServer.Services;
using AirwaysMergeSafeServer.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Linq;

namespace AirwaysMergeSafeServer.Controllers;

/// <summary>
/// Phase 7:
///   A1  — BuildSimulatedSegments uses Random.Shared (no more new Random()).
///   P6  — Index passes RecentEvents + ground/air counts to Traffic3DViewModel.
///   P6  — AirScene action enriches view with classified event breakdown.
/// </summary>
public class Traffic3DController : Controller
{
    private readonly AppDbContext        _db;
    private readonly IConfiguration      _cfg;
    private readonly IMemoryCache        _cache;
    private readonly IHttpClientFactory  _httpFactory;
    private readonly IVehicleRegistry    _vehicleRegistry;

    private static readonly Regex _bboxRegex = new(
        @"^-?\d{1,3}(\.\d+)?,-?\d{1,3}(\.\d+)?,-?\d{1,3}(\.\d+)?,-?\d{1,3}(\.\d+)?$",
        RegexOptions.Compiled);

    public Traffic3DController(AppDbContext db, IConfiguration cfg,
        IMemoryCache cache, IHttpClientFactory httpFactory,
        IVehicleRegistry vehicleRegistry)
    { _db = db; _cfg = cfg; _cache = cache; _httpFactory = httpFactory; _vehicleRegistry = vehicleRegistry; }

    public async Task<IActionResult> Index(string? highwayId)
    {
        var highways  = await _db.Highways.AsNoTracking().Where(h => h.IsActive).OrderBy(h => h.Name).ToListAsync();
        highwayId ??= HttpContext.Session.GetString("HighwayId") ?? highways.FirstOrDefault()?.HighwayId;
        if (highwayId != null) HttpContext.Session.SetString("HighwayId", highwayId);

        var zones   = await _db.MergeZones.AsNoTracking().Where(z => z.HighwayId == highwayId).ToListAsync();
        var sensors = await _db.SensorDevices.AsNoTracking().Where(d => d.HighwayId == highwayId).ToListAsync();
        var zoneIds = zones.Select(z => z.ZoneId).ToList();
        var servers = await _db.SwitchServers.AsNoTracking()
            .Where(s => s.ZoneId != null && zoneIds.Contains(s.ZoneId))
            .OrderBy(s => s.ZoneId).ThenBy(s => s.ServerName).ToListAsync();

        // Task 10: ground-only query — includes ground vehicles OR explicitly non-flycar events
        // Spec: VehicleMode='ground' OR IsAirFlyCar='N' (OR is intentional: include any record
        // that is either a ground-mode vehicle or explicitly flagged as non-flycar)
        var recentEvents = await _db.VehicleEvents.AsNoTracking()
            .Where(e => e.HighwayId == highwayId
                     && (e.VehicleMode == "ground" || e.IsAirFlyCar == "N"))
            .OrderByDescending(e => e.CreatedDate)
            .Take(80)
            .Select(e => new {
                e.Id, e.VehicleId, e.EventType, e.ZoneId,
                e.SpeedMph, e.Latitude, e.Longitude, e.AltitudeMeters,
                e.VehicleMode, e.VehicleCategory, e.VehicleClassJson,
                e.IsAirFlyCar, e.CreatedDate
            })
            .ToListAsync();

        var groundCount = recentEvents.Count;
        var airCount    = 0; // air vehicles are in AirScene (/AirScene)

        // Serialize brand logos from VehicleRegistry for 3D scene rendering
        var brandLogos = _vehicleRegistry.All
            .GroupBy(v => v.Make)
            .ToDictionary(g => g.Key, g => g.First().BrandLogo);

        return View(new Traffic3DViewModel
        {
            Highways          = highways,
            SelectedHighwayId = highwayId,
            Zones             = zones,
            SwitchServers     = servers,
            Sensors           = sensors,
            TomTomApiKey      = _cfg["TomTomApiKey"],
            RecentEventsJson  = JsonSerializer.Serialize(recentEvents),
            GroundCount       = groundCount,
            AirCount          = airCount,
            BrandLogosJson    = JsonSerializer.Serialize(brandLogos)
        });
    }

    /// <summary>
    /// Task 10: AirScene is now fully independent at /AirScene.
    /// This action redirects legacy links gracefully.
    /// </summary>
    [HttpGet]
    public IActionResult AirScene(string? highwayId)
        => RedirectToAction("Index", "AirScene", new { highwayId });


    // ═══════════════════════════════════════════════════════════════════
    // Server-side animation data endpoints
    // ═══════════════════════════════════════════════════════════════════
    // The client polls these endpoints for pre-validated vehicle data
    // and highway/zone coordinates. All coordinate validation happens
    // server-side — the client just paints what the server returns.

    /// <summary>
    /// Returns validated animation data for the selected highway/zone/server.
    /// The client polls this instead of /api/events/live.
    /// Response: { highwayCoords, vehicles, bounds, isEW }
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAnimationData(
        string? highwayId, string? zoneId, string? serverId, string? mode = "ground")
    {
        highwayId ??= HttpContext.Session.GetString("HighwayId") ?? "";
        if (string.IsNullOrEmpty(highwayId))
            return Json(new { highwayCoords = Array.Empty<object>(), vehicles = Array.Empty<object>(), bounds = (object?)null, isEW = true });

        // 1. Fetch ALL zones for this highway (for bridge path)
        var zones = await _db.MergeZones.AsNoTracking()
            .Where(z => z.HighwayId == highwayId && z.Latitude.HasValue && z.Longitude.HasValue)
            .OrderBy(z => z.Longitude) // rough sort — client re-sorts by axis
            .Select(z => new {
                zoneId   = z.ZoneId,
                zoneName = z.ZoneName,
                lat      = z.Latitude!.Value,
                lon      = z.Longitude!.Value,
                highwayId = z.HighwayId,
                radius   = z.GeofenceRadius,
                status   = z.Status
            })
            .ToListAsync();

        if (zones.Count == 0)
            return Json(new { highwayCoords = Array.Empty<object>(), vehicles = Array.Empty<object>(), bounds = (object?)null, isEW = true });

        // 2. Determine if highway is E-W or N-S
        bool isEW = !highwayId.Contains("I35") && !highwayId.Contains("I45") && !highwayId.Contains("I25");

        // 3. Compute geographic bounds
        var lats = zones.Select(z => z.lat).ToList();
        var lons = zones.Select(z => z.lon).ToList();
        double minLat = lats.Min(), maxLat = lats.Max();
        double minLon = lons.Min(), maxLon = lons.Max();

        // If a specific zone is selected, tighten bounds around that zone
        double bufAlong = 0.045, bufCross = 0.004;
        if (!string.IsNullOrEmpty(zoneId))
        {
            var selZone = zones.FirstOrDefault(z => z.zoneId == zoneId);
            if (selZone != null)
            {
                if (isEW)
                {
                    minLat = selZone.lat - bufCross; maxLat = selZone.lat + bufCross;
                    minLon = selZone.lon - bufAlong; maxLon = selZone.lon + bufAlong;
                }
                else
                {
                    minLat = selZone.lat - bufAlong; maxLat = selZone.lat + bufAlong;
                    minLon = selZone.lon - bufCross; maxLon = selZone.lon + bufCross;
                }
            }
        }
        else
        {
            // All zones — add 15% padding
            double padLat = (maxLat - minLat) * 0.15; if (padLat < 0.005) padLat = 0.005;
            double padLon = (maxLon - minLon) * 0.15; if (padLon < 0.005) padLon = 0.005;
            minLat -= padLat; maxLat += padLat;
            minLon -= padLon; maxLon += padLon;
        }

        // 4. Fetch vehicle events for this highway
        var eventsQuery = _db.VehicleEvents.AsNoTracking()
            .Where(e => e.HighwayId == highwayId
                     && (e.VehicleMode == "ground" || e.IsAirFlyCar == "N"));

        if (!string.IsNullOrEmpty(zoneId))
            eventsQuery = eventsQuery.Where(e => e.ZoneId == zoneId);
        else if (!string.IsNullOrEmpty(serverId))
        {
            var srvZone = await _db.SwitchServers.AsNoTracking()
                .Where(s => s.ServerId == serverId).Select(s => s.ZoneId).FirstOrDefaultAsync();
            if (!string.IsNullOrEmpty(srvZone))
                eventsQuery = eventsQuery.Where(e => e.ZoneId == srvZone);
        }

        var rawEvents = await eventsQuery
            .OrderByDescending(e => e.CreatedDate)
            .Take(100)
            .Select(e => new {
                e.Id, e.VehicleId, e.EventType, e.ZoneId,
                e.SpeedMph, e.Latitude, e.Longitude, e.AltitudeMeters,
                e.VehicleMode, e.VehicleCategory, e.VehicleClassJson,
                e.IsAirFlyCar, e.CreatedDate, e.HighwayId
            })
            .ToListAsync();

        // 5. Validate each vehicle record's coordinates
        var validatedVehicles = new List<object>();
        foreach (var ev in rawEvents)
        {
            double lat = ev.Latitude ?? 0;
            double lon = ev.Longitude ?? 0;
            double speedMph = ev.SpeedMph ?? 0;
            bool needsSnap = false;

            // Check for missing/zero GPS
            if (lat == 0 && lon == 0 || double.IsNaN(lat) || double.IsNaN(lon))
                needsSnap = true;

            // Check against bounds
            if (!needsSnap && (lat < minLat || lat > maxLat || lon < minLon || lon > maxLon))
                needsSnap = true;

            // Find nearest zone for snapping
            if (needsSnap)
            {
                var nearestZone = zones
                    .OrderBy(z => Math.Sqrt(Math.Pow(z.lat - (lat > 0 ? lat : zones.First().lat), 2) + Math.Pow(z.lon - (lon > 0 ? lon : zones.First().lon), 2)))
                    .First();

                var rng = Random.Shared;
                double jLat, jLon;
                if (isEW)
                {
                    jLat = (rng.NextDouble() - 0.5) * 0.0008;  // ±44m cross-axis
                    jLon = (rng.NextDouble() - 0.5) * 0.025;    // ±2.5km along-axis
                }
                else
                {
                    jLat = (rng.NextDouble() - 0.5) * 0.025;
                    jLon = (rng.NextDouble() - 0.5) * 0.0008;
                }
                lat = nearestZone.lat + jLat;
                lon = nearestZone.lon + jLon;
            }

            // Validate direction — snap to highway axis
            int direction = 90; // default E-W
            if (!isEW)
            {
                // N-S highway: 0 (north) or 180 (south)
                direction = Random.Shared.Next(2) == 0 ? 0 : 180;
            }
            else
            {
                // E-W highway: 90 (east) or 270 (west)
                direction = Random.Shared.Next(2) == 0 ? 90 : 270;
            }

            validatedVehicles.Add(new
            {
                id = ev.Id,
                vehicle_id = ev.VehicleId,
                zone_id = ev.ZoneId ?? "",
                highway_id = ev.HighwayId ?? highwayId,
                speed_mph = speedMph,
                latitude = Math.Round(lat, 6),
                longitude = Math.Round(lon, 6),
                direction = direction,
                vehicle_mode = ev.VehicleMode ?? "ground",
                vehicle_category = ev.VehicleCategory ?? "",
                vehicle_class_json = ev.VehicleClassJson ?? "",
                is_air_fly_car = ev.IsAirFlyCar ?? "N",
                created_date = ev.CreatedDate,
                validated = needsSnap // for debugging: true if coords were snapped
            });
        }

        // 6. Return sorted zone coordinates for the bridge path
        var sortedZones = isEW
            ? zones.OrderBy(z => z.lon).ToList()
            : zones.OrderBy(z => z.lat).ToList();

        return Json(new
        {
            highwayCoords = sortedZones,
            vehicles = validatedVehicles,
            bounds = new { minLat, maxLat, minLon, maxLon, isEW, tight = !string.IsNullOrEmpty(zoneId) },
            isEW = isEW,
            highwayId = highwayId,
            selectedZoneId = zoneId ?? "",
            selectedServerId = serverId ?? "",
            generatedAt = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Validates a single vehicle record's coordinates against the selected
    /// highway/zone bounds. Called by the simulation broadcast pipeline.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> ValidateVehicleCoordinates(
        [FromBody] VehicleCoordRequest req)
    {
        highwayId ??= HttpContext.Session.GetString("HighwayId") ?? req.HighwayId ?? "";
        if (string.IsNullOrEmpty(highwayId))
            return Json(new { valid = true, lat = req.Lat, lon = req.Lon, snapped = false });

        var zones = await _db.MergeZones.AsNoTracking()
            .Where(z => z.HighwayId == highwayId && z.Latitude.HasValue)
            .Select(z => new { z.ZoneId, z.Latitude, z.Longitude, z.HighwayId })
            .ToListAsync();
        if (zones.Count == 0) return Json(new { valid = true, lat = req.Lat, lon = req.Lon, snapped = false });

        bool isEW = !highwayId.Contains("I35") && !highwayId.Contains("I45") && !highwayId.Contains("I25");

        double lat = req.Lat, lon = req.Lon;
        bool snapped = false;

        if (lat == 0 && lon == 0) { snapped = true; }

        // Find nearest zone
        var nearest = zones.OrderBy(z =>
            Math.Sqrt(Math.Pow((z.Latitude ?? 0) - lat, 2) + Math.Pow((z.Longitude ?? 0) - lon, 2))).First();

        double zoneLat = nearest.Latitude ?? 0, zoneLon = nearest.Longitude ?? 0;
        double dist = Math.Sqrt(Math.Pow(lat - zoneLat, 2) + Math.Pow(lon - zoneLon, 2));

        if (dist > 0.08) snapped = true; // >8km from nearest zone

        if (snapped)
        {
            var rng = Random.Shared;
            if (isEW)
            {
                lat = zoneLat + (rng.NextDouble() - 0.5) * 0.0008;
                lon = zoneLon + (rng.NextDouble() - 0.5) * 0.025;
            }
            else
            {
                lat = zoneLat + (rng.NextDouble() - 0.5) * 0.025;
                lon = zoneLon + (rng.NextDouble() - 0.5) * 0.0008;
            }
        }

        // Clamp to selected zone bounds if zoneId is provided
        if (!string.IsNullOrEmpty(req.ZoneId))
        {
            var selZone = zones.FirstOrDefault(z => z.ZoneId == req.ZoneId);
            if (selZone != null)
            {
                double zLat = selZone.Latitude ?? 0, zLon = selZone.Longitude ?? 0;
                if (isEW)
                {
                    lat = Math.Max(zLat - 0.004, Math.Min(zLat + 0.004, lat));
                    lon = Math.Max(zLon - 0.045, Math.Min(zLon + 0.045, lon));
                }
                else
                {
                    lat = Math.Max(zLat - 0.045, Math.Min(zLat + 0.045, lat));
                    lon = Math.Max(zLon - 0.004, Math.Min(zLon + 0.004, lon));
                }
            }
        }

        return Json(new { valid = !snapped, lat = Math.Round(lat, 6), lon = Math.Round(lon, 6), snapped, zoneId = nearest.ZoneId });
    }

    public class VehicleCoordRequest
    {
        public double Lat { get; set; }
        public double Lon { get; set; }
        public string? HighwayId { get; set; }
        public string? ZoneId { get; set; }
        public string? VehicleId { get; set; }
    }

    [HttpGet]
    public async Task<IActionResult> GetTrafficSegments(string highwayId, string? bbox)
    {
        if (!string.IsNullOrEmpty(bbox) && !_bboxRegex.IsMatch(bbox))
            return BadRequest("Invalid bbox parameter.");

        var cacheKey = $"traffic_{highwayId}_{bbox ?? ""}";
        if (_cache.TryGetValue(cacheKey, out object? cached)) return Json(cached);

        var tomTomKey = _cfg["TomTomApiKey"];
        object segments;

        if (!string.IsNullOrWhiteSpace(tomTomKey))
        {
            try
            {
                var client = _httpFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(8);
                var resolvedBbox = bbox ?? (highwayId == "I20-TX"
                    ? "32.72,-97.15,32.80,-96.95"
                    : highwayId == "I35-TX"
                    ? "31.05,-97.38,31.60,-97.08"
                    : "29.70,-95.80,29.90,-95.30");

                if (!_bboxRegex.IsMatch(resolvedBbox))
                    return BadRequest("Invalid bbox parameter.");

                var url = $"https://api.tomtom.com/traffic/services/4/flowSegmentData/absolute/10/json?key={tomTomKey}&bbox={resolvedBbox}";
                var resp = await client.GetAsync(url);
                segments = resp.IsSuccessStatusCode
                    ? new { source = "tomtom", data = JsonSerializer.Deserialize<object>(await resp.Content.ReadAsStringAsync()) }
                    : BuildSimulatedSegments(highwayId);
            }
            catch { segments = BuildSimulatedSegments(highwayId); }
        }
        else
        {
            segments = BuildSimulatedSegments(highwayId);
        }

        _cache.Set(cacheKey, segments, TimeSpan.FromMinutes(5));
        return Json(segments);
    }

    // A1 FIX: Random.Shared
    private static object BuildSimulatedSegments(string highwayId)
    {
        var rng  = Random.Shared;
        var names = highwayId == "I20-TX"
            ? new[] { "Dallas West","Grand Prairie","Arlington","Fort Worth East","Mesquite","Duncanville","DeSoto","Lancaster" }
            : highwayId == "I35-TX"
            ? new[] { "Waco North","Temple","Georgetown","Round Rock","Austin North","San Marcos","New Braunfels","San Antonio" }
            : new[] { "Houston West","Katy","Sugar Land","Houston East","Beaumont","Orange","Baytown","Pasadena" };

        return new {
            source      = "simulated",
            highway     = highwayId,
            generatedAt = DateTime.UtcNow,
            segments    = names.Select((name, i) => new {
                id                = $"SEG-{i+1:D3}",
                name,
                speedMph          = rng.Next(15, 75),
                freeFlowSpeedMph  = 70,
                congestion        = rng.Next(0, 5) switch { 4=>"heavy", 3=>"moderate", _=>"free" },
                travelTimeSeconds = rng.Next(60, 600)
            }).ToList()
        };
    }
}
