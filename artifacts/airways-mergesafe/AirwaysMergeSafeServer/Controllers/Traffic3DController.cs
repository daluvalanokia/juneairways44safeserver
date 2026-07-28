using AirwaysMergeSafeServer.Data;
using AirwaysMergeSafeServer.Infrastructure;
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
        string? highwayId, string? zoneId, string? serverId, string? mode = "ground",
        bool simOnly = false)
    {
        TraceLogger.Enter("Traffic3D", nameof(GetAnimationData), $"hw={highwayId} z={zoneId} s={serverId}");
        highwayId ??= HttpContext.Session.GetString("HighwayId") ?? "";
        if (!string.IsNullOrEmpty(highwayId))
            HttpContext.Session.SetString("HighwayId", highwayId);

        // ── Require all 3 parameters (highway + zone + server) ──
        // If any is missing, return zones + servers (for dropdowns) but NO vehicles.
        // Animation only loads when all 3 are provided.
        bool hasAll3 = !string.IsNullOrEmpty(highwayId)
                    && !string.IsNullOrEmpty(zoneId)
                    && !string.IsNullOrEmpty(serverId);
        if (!hasAll3)
        {
            TraceLogger.Info("Traffic3D", nameof(GetAnimationData),
                $"Incomplete: hw={highwayId} z={zoneId} s={serverId} — returning zones/servers only");
            if (string.IsNullOrEmpty(highwayId))
                return Json(new { highwayCoords = Array.Empty<object>(), vehicles = Array.Empty<object>(),
                    bounds = (object?)null, isEW = true, servers = Array.Empty<object>() });
            // Return zones + servers for dropdown population, but no vehicles
            var zonesOnly = await _db.MergeZones.AsNoTracking()
                .Where(z => z.HighwayId == highwayId && z.Latitude.HasValue && z.Longitude.HasValue)
                .OrderBy(z => z.Longitude)
                .Select(z => new {
                    zoneId = z.ZoneId, zoneName = z.ZoneName,
                    lat = z.Latitude!.Value, lon = z.Longitude!.Value,
                    highwayId = z.HighwayId, radius = z.GeofenceRadius
                }).ToListAsync();
            var earlyZoneIds = zonesOnly.Select(z => z.zoneId).ToList();
            var serversOnly = await _db.SwitchServers.AsNoTracking()
                .Where(s => s.ZoneId != null && earlyZoneIds.Contains(s.ZoneId))
                .OrderBy(s => s.ZoneId).ThenBy(s => s.ServerName)
                .Select(s => new { serverId = s.ServerId, serverName = s.ServerName, zoneId = s.ZoneId ?? "" })
                .ToListAsync();
            var earlyLats = zonesOnly.Select(z => (double)z.lat).ToList();
            var earlyLons = zonesOnly.Select(z => (double)z.lon).ToList();
            object? boundsOnly = earlyLats.Count > 0 ? new {
                minLat = earlyLats.Min() - 0.03, maxLat = earlyLats.Max() + 0.03,
                minLon = earlyLons.Min() - 0.03, maxLon = earlyLons.Max() + 0.03
            } : null;
            var isEwOnly = !highwayId.Contains("I35") && !highwayId.Contains("I45") && !highwayId.Contains("I25");

            // ── Also compute bridgePath + hwBearing for the zone-only fetch ──
            // This ensures dropdown-population fetches also return the dense
            // bridge path and real bearing, so the bridge is correct from the
            // very first frame — before zone/server are even selected.
            var sortedEarly = isEwOnly
                ? zonesOnly.OrderBy(z => z.lon).ToList()
                : zonesOnly.OrderBy(z => z.lat).ToList();
            double earlyBearing = 90.0;
            if (sortedEarly.Count >= 2)
            {
                var e0 = sortedEarly[0]; var e1 = sortedEarly[^1];
                double eMidLat = (e0.lat + e1.lat) / 2.0;
                double eCosLat = Math.Cos(eMidLat * Math.PI / 180.0);
                double eDLat = e1.lat - e0.lat; double eDLon = e1.lon - e0.lon;
                earlyBearing = Math.Atan2(eDLon * eCosLat, eDLat) * 180.0 / Math.PI;
                if (earlyBearing < 0) earlyBearing += 360;
            }
            var earlyBridgePath = new List<object>();
            if (sortedEarly.Count >= 2)
            {
                var ez0 = sortedEarly[0]; var ez1 = sortedEarly[1];
                double edLat = ez0.lat - ez1.lat; double edLon = ez0.lon - ez1.lon;
                double edLen = Math.Sqrt(edLat*edLat + edLon*edLon);
                if (edLen > 0) { edLat /= edLen; edLon /= edLen; }
                for (int ei = 10; ei >= 1; ei--) {
                    double t = 0.027 * ei / 10;
                    earlyBridgePath.Add(new { lat = Math.Round(ez0.lat + edLat*t, 6), lon = Math.Round(ez0.lon + edLon*t, 6) });
                }
                for (int si = 0; si < sortedEarly.Count - 1; si++) {
                    var zA = sortedEarly[si]; var zB = sortedEarly[si + 1];
                    double segLat = zB.lat - zA.lat; double segLon = zB.lon - zA.lon;
                    double segLen = Math.Sqrt(segLat*segLat + segLon*segLon);
                    int steps = Math.Max(3, (int)(segLen / 0.003));
                    for (int st = 0; st < steps; st++) {
                        double t = (double)st / steps;
                        earlyBridgePath.Add(new { lat = Math.Round(zA.lat + segLat*t, 6), lon = Math.Round(zA.lon + segLon*t, 6) });
                    }
                }
                earlyBridgePath.Add(new { lat = Math.Round(sortedEarly[^1].lat, 6), lon = Math.Round(sortedEarly[^1].lon, 6) });
                var ezN1 = sortedEarly[^1]; var ezN2 = sortedEarly[^2];
                double ldLat = ezN1.lat - ezN2.lat; double ldLon = ezN1.lon - ezN2.lon;
                double ldLen = Math.Sqrt(ldLat*ldLat + ldLon*ldLon);
                if (ldLen > 0) { ldLat /= ldLen; ldLon /= ldLen; }
                for (int li = 1; li <= 10; li++) {
                    double t = 0.027 * li / 10;
                    earlyBridgePath.Add(new { lat = Math.Round(ezN1.lat + ldLat*t, 6), lon = Math.Round(ezN1.lon + ldLon*t, 6) });
                }
            }

            return Json(new {
                highwayCoords = zonesOnly, vehicles = Array.Empty<object>(),
                bounds = boundsOnly, isEW = isEwOnly, servers = serversOnly,
                bridgePath = earlyBridgePath,
                hwBearing = Math.Round(earlyBearing, 2)
            });
        }

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
        {
            TraceLogger.Info("Traffic3D", nameof(GetAnimationData), $"No zones for hw={highwayId}");
            return Json(new { highwayCoords = Array.Empty<object>(), vehicles = Array.Empty<object>(), bounds = (object?)null, isEW = true, servers = Array.Empty<object>() });
        }
        // 2. Determine if highway is E-W or N-S
        bool isEW = !highwayId.Contains("I35") && !highwayId.Contains("I45") && !highwayId.Contains("I25");
        TraceLogger.Info("Traffic3D", nameof(GetAnimationData), $"Highway={highwayId} zones={zones.Count} isEW={isEW}");

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
        //    Zone/server filter tightens the query, but if it returns 0 vehicles
        //    we fall back to highway-level so the animation never goes empty.
        var zoneFilter = zoneId;
        if (string.IsNullOrEmpty(zoneFilter) && !string.IsNullOrEmpty(serverId))
            zoneFilter = await _db.SwitchServers.AsNoTracking()
                .Where(s => s.ServerId == serverId).Select(s => s.ZoneId).FirstOrDefaultAsync();

        var baseQuery = _db.VehicleEvents.AsNoTracking()
            .Where(e => e.HighwayId == highwayId
                     && (e.VehicleMode == "ground" || e.IsAirFlyCar == "N"));

        // When simOnly=true, only fetch recent events (last 10 min) — these are
        // the simulation's events. Also filter by the sim's exact zone/server.
        if (simOnly)
        {
            var cutoff = DateTime.UtcNow.AddMinutes(-10);
            baseQuery = baseQuery.Where(e => e.CreatedDate >= cutoff);
        }

        var eventsQuery = baseQuery;
        if (!string.IsNullOrEmpty(zoneFilter))
            eventsQuery = eventsQuery.Where(e => e.ZoneId == zoneFilter);

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

        // Fallback: if zone/server filter returned 0 vehicles, retry highway-level
        // so the animation stays populated. Zone selection still tightens map bounds.
        // SKIP this fallback when simOnly=true — we want ONLY the sim's exact
        // zone/server events, not highway-wide data.
        if (rawEvents.Count == 0 && !string.IsNullOrEmpty(zoneFilter) && !simOnly)
        {
            TraceLogger.Info("Traffic3D", nameof(GetAnimationData),
                $"Zone {zoneFilter} has 0 vehicles — falling back to highway {highwayId}");
            rawEvents = await baseQuery
                .OrderByDescending(e => e.CreatedDate)
                .Take(100)
                .Select(e => new {
                    e.Id, e.VehicleId, e.EventType, e.ZoneId,
                    e.SpeedMph, e.Latitude, e.Longitude, e.AltitudeMeters,
                    e.VehicleMode, e.VehicleCategory, e.VehicleClassJson,
                    e.IsAirFlyCar, e.CreatedDate, e.HighwayId
                })
                .ToListAsync();
        }

        // 5. Prepare validated vehicles list
        var validatedVehicles = new List<object>();

        // ── ULTIMATE FALLBACK: If still 0 vehicles for the highway, generate
        //    synthetic vehicles from zone coordinates so the animation always
        //    has something to show. This covers highways with no VehicleEvents
        //    in the DB at all.
        // SKIP when simOnly=true — simulation data should not show synthetic vehicles.
        if (rawEvents.Count == 0 && !simOnly)
        {
            TraceLogger.Info("Traffic3D", nameof(GetAnimationData),
                $"No VehicleEvents for {highwayId} — generating synthetic vehicles from {zones.Count} zones");
            var rng = Random.Shared;
            int syntheticId = 1;
            var categories = new[] { "Sedan", "SUV", "Pickup", "Coupe", "Van" };
            foreach (var zn in zones)
            {
                int vehiclesPerZone = rng.Next(3, 5);
                for (int v = 0; v < vehiclesPerZone; v++)
                {
                    double jLat, jLon;
                    if (isEW)
                    {
                        jLat = (rng.NextDouble() - 0.5) * 0.001;
                        jLon = (rng.NextDouble() - 0.5) * 0.020;
                    }
                    else
                    {
                        jLat = (rng.NextDouble() - 0.5) * 0.020;
                        jLon = (rng.NextDouble() - 0.5) * 0.001;
                    }
                    int sDir = isEW
                        ? (rng.Next(2) == 0 ? 90 : 270)
                        : (rng.Next(2) == 0 ? 0 : 180);
                    validatedVehicles.Add(new
                    {
                        id = syntheticId,
                        vehicle_id = "SYN-" + syntheticId.ToString("D4"),
                        zone_id = zn.zoneId,
                        highway_id = highwayId,
                        speed_mph = (double)rng.Next(35, 75),
                        latitude = Math.Round(zn.lat + jLat, 6),
                        longitude = Math.Round(zn.lon + jLon, 6),
                        direction = sDir,
                        vehicle_mode = "ground",
                        vehicle_category = categories[rng.Next(categories.Length)],
                        vehicle_class_json = "",
                        is_air_fly_car = "N",
                        created_date = DateTime.UtcNow,
                        validated = true
                    });
                    syntheticId++;
                }
            }
            TraceLogger.Info("Traffic3D", nameof(GetAnimationData),
                $"Generated {validatedVehicles.Count} synthetic vehicles for {highwayId}");
            goto ReturnResult;
        }
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
                vehicle_make = _extractMakeFromPayload(ev.Payload),
                is_air_fly_car = ev.IsAirFlyCar ?? "N",
                created_date = ev.CreatedDate,
                validated = needsSnap // for debugging: true if coords were snapped
            });
        }

        ReturnResult:

        // ── Helper: extract vehicle_make from raw payload JSON ─────────────
        static string _extractMakeFromPayload(string? payloadJson)
        {
            if (string.IsNullOrEmpty(payloadJson)) return "";
            try
            {
                using var doc = JsonDocument.Parse(payloadJson);
                if (doc.RootElement.TryGetProperty("vehicle_make", out var mk))
                    return mk.GetString() ?? "";
            }
            catch { }
            return "";
        }

        // 6. Sort zone coordinates for the bridge path by road axis
        var sortedZones = isEW
            ? zones.OrderBy(z => z.lon).ToList()
            : zones.OrderBy(z => z.lat).ToList();

        // 7. Compute real highway bearing from actual zone GPS
        //    bearing = atan2(Δlon * cos(latMid), Δlat) → degrees from North
        double hwBearing = 90.0;
        if (sortedZones.Count >= 2)
        {
            var zFirst = sortedZones[0];
            var zLast  = sortedZones[^1];
            double midLat = (zFirst.lat + zLast.lat) / 2.0;
            double cosLat = Math.Cos(midLat * Math.PI / 180.0);
            double dLat   = zLast.lat - zFirst.lat;
            double dLon   = zLast.lon - zFirst.lon;
            hwBearing = Math.Atan2(dLon * cosLat, dLat) * 180.0 / Math.PI;
            if (hwBearing < 0) hwBearing += 360;
        }

        // 8. Build dense bridge path: interpolate between zone anchors at
        //    ~300m intervals and extend 3 km beyond first/last zone so the
        //    polyline always fills the visible map area.
        //    Each point: { lat, lon } — client uses this as ground truth
        //    for vehicle positioning and movement bearing.
        var bridgePath = new List<object>();
        const int extSteps = 10;
        const double extDeg = 0.027;   // ~3 km extension beyond each end
        const double interpStep = 0.003; // ~330 m interpolation interval

        if (sortedZones.Count >= 2)
        {
            // Extension before first zone (project backward along segment direction)
            var z0 = sortedZones[0]; var z1 = sortedZones[1];
            double eDlat = z0.lat - z1.lat; double eDlon = z0.lon - z1.lon;
            double eLen  = Math.Sqrt(eDlat*eDlat + eDlon*eDlon);
            if (eLen > 0) { eDlat /= eLen; eDlon /= eLen; }
            for (int ei = extSteps; ei >= 1; ei--)
            {
                double t = extDeg * ei / extSteps;
                bridgePath.Add(new { lat = Math.Round(z0.lat + eDlat*t, 6), lon = Math.Round(z0.lon + eDlon*t, 6) });
            }

            // Interpolate between consecutive zone waypoints
            for (int si = 0; si < sortedZones.Count - 1; si++)
            {
                var zA = sortedZones[si]; var zB = sortedZones[si + 1];
                double segLat = zB.lat - zA.lat, segLon = zB.lon - zA.lon;
                double segLen = Math.Sqrt(segLat*segLat + segLon*segLon);
                int steps = Math.Max(3, (int)(segLen / interpStep));
                for (int st = 0; st < steps; st++)
                {
                    double t = (double)st / steps;
                    bridgePath.Add(new { lat = Math.Round(zA.lat + segLat*t, 6), lon = Math.Round(zA.lon + segLon*t, 6) });
                }
            }
            bridgePath.Add(new { lat = Math.Round(sortedZones[^1].lat, 6), lon = Math.Round(sortedZones[^1].lon, 6) });

            // Extension after last zone
            var zN1 = sortedZones[^1]; var zN2 = sortedZones[^2];
            double lDlat = zN1.lat - zN2.lat; double lDlon = zN1.lon - zN2.lon;
            double lLen  = Math.Sqrt(lDlat*lDlat + lDlon*lDlon);
            if (lLen > 0) { lDlat /= lLen; lDlon /= lLen; }
            for (int li = 1; li <= extSteps; li++)
            {
                double t = extDeg * li / extSteps;
                bridgePath.Add(new { lat = Math.Round(zN1.lat + lDlat*t, 6), lon = Math.Round(zN1.lon + lDlon*t, 6) });
            }
        }
        else if (sortedZones.Count == 1)
        {
            // Single zone — extend along bearing
            var zOnly = sortedZones[0];
            double bRad = hwBearing * Math.PI / 180.0;
            double cosZ = Math.Cos(zOnly.lat * Math.PI / 180.0);
            for (int si = -extSteps; si <= extSteps; si++)
            {
                double t = extDeg * si / extSteps;
                bridgePath.Add(new {
                    lat = Math.Round(zOnly.lat + Math.Cos(bRad)*t, 6),
                    lon = Math.Round(zOnly.lon + Math.Sin(bRad)*t/cosZ, 6)
                });
            }
        }

        TraceLogger.Info("Traffic3D", nameof(GetAnimationData),
            $"bridgePath={bridgePath.Count} pts hwBearing={hwBearing:F1}° isEW={isEW}");

        // 9. Fetch switch servers for this highway
        var zoneIds = zones.Select(z => z.zoneId).ToList();
        var servers = await _db.SwitchServers.AsNoTracking()
            .Where(s => s.ZoneId != null && zoneIds.Contains(s.ZoneId))
            .OrderBy(s => s.ZoneId).ThenBy(s => s.ServerName)
            .Select(s => new {
                serverId   = s.ServerId,
                serverName = s.ServerName,
                zoneId     = s.ZoneId ?? ""
            })
            .ToListAsync();

        return Json(new
        {
            highwayCoords    = sortedZones,
            bridgePath       = bridgePath,
            hwBearing        = Math.Round(hwBearing, 2),
            vehicles         = validatedVehicles,
            bounds           = new { minLat, maxLat, minLon, maxLon, isEW, tight = !string.IsNullOrEmpty(zoneId) },
            isEW             = isEW,
            highwayId        = highwayId,
            selectedZoneId   = zoneId ?? "",
            selectedServerId = serverId ?? "",
            servers          = servers,
            generatedAt      = DateTime.UtcNow
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
        var highwayId = req.HighwayId ?? HttpContext.Session.GetString("HighwayId") ?? "";
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
