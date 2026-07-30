using AirwaysMergeSafeServer.Data;
using AirwaysMergeSafeServer.Services;
using AirwaysMergeSafeServer.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace AirwaysMergeSafeServer.Controllers;

/// <summary>
/// 3D Air Scene controller — serves air-domain vehicles (VehicleMode="air" OR IsAirFlyCar="Y").
/// Duplicates Traffic3D functionality: GetAnimationData, bridge path, bearing, zone pins.
/// Route: /AirScene
/// </summary>
public class AirSceneController : Controller
{
    private readonly AppDbContext  _db;
    private readonly IConfiguration _cfg;

    public AirSceneController(AppDbContext db, IConfiguration cfg)
    { _db = db; _cfg = cfg; }

    [HttpGet]
    public async Task<IActionResult> Index(string? highwayId)
    {
        var highways = await _db.Highways.AsNoTracking()
            .Where(h => h.IsActive).OrderBy(h => h.Name).ToListAsync();
        highwayId ??= HttpContext.Session.GetString("HighwayId")
                      ?? highways.FirstOrDefault()?.HighwayId;
        if (highwayId != null) HttpContext.Session.SetString("HighwayId", highwayId);

        var zones   = await _db.MergeZones.AsNoTracking()
                          .Where(z => z.HighwayId == highwayId).ToListAsync();
        var sensors = await _db.SensorDevices.AsNoTracking()
                          .Where(d => d.HighwayId == highwayId).ToListAsync();
        var zoneIds = zones.Select(z => z.ZoneId).ToList();
        var servers = await _db.SwitchServers.AsNoTracking()
                          .Where(s => s.ZoneId != null && zoneIds.Contains(s.ZoneId))
                          .OrderBy(s => s.ZoneId).ThenBy(s => s.ServerName).ToListAsync();

        var recentEvents = await _db.VehicleEvents.AsNoTracking()
            .Where(e => e.HighwayId == highwayId
                     && (e.VehicleMode == "air" || e.IsAirFlyCar == "Y"))
            .OrderByDescending(e => e.CreatedDate)
            .Take(120)
            .Select(e => new {
                e.Id, e.VehicleId, e.EventType, e.ZoneId,
                e.SpeedMph, e.Latitude, e.Longitude, e.AltitudeMeters,
                e.VehicleMode, e.VehicleCategory, e.VehicleClassJson,
                e.IsAirFlyCar, e.CreatedDate
            })
            .ToListAsync();

        var groundCount = recentEvents.Count(e => e.VehicleMode == "ground" && e.IsAirFlyCar != "Y");
        var airCount    = recentEvents.Count(e => e.VehicleMode == "air" || e.IsAirFlyCar == "Y");
        var catBreakdown = recentEvents
            .GroupBy(e => e.VehicleCategory)
            .ToDictionary(g => g.Key, g => g.Count());

        return View(new AirSceneViewModel
        {
            Highways           = highways,
            SelectedHighwayId  = highwayId,
            Zones              = zones,
            SwitchServers      = servers,
            Sensors            = sensors,
            RecentEventsJson   = JsonSerializer.Serialize(recentEvents),
            GroundCount        = groundCount,
            AirCount           = airCount,
            CategoryBreakdown  = catBreakdown,
            AirSceneAlertsJson = SettingsController.LoadAirSceneAlertsJson()
        });
    }

    // ══════════════════════════════════════════════════════════════════════
    // GetAnimationData — duplicates Traffic3D's endpoint for air vehicles
    // Returns: highwayCoords, bridgePath, hwBearing, vehicles, bounds, servers
    // ══════════════════════════════════════════════════════════════════════
    [HttpGet]
    public async Task<IActionResult> GetAnimationData(
        string? highwayId, string? zoneId, string? serverId, string? mode = "air",
        bool simOnly = false)
    {
        TraceLogger.Enter("AirScene", nameof(GetAnimationData), $"hw={highwayId} z={zoneId} s={serverId}");
        highwayId ??= HttpContext.Session.GetString("HighwayId") ?? "";
        if (!string.IsNullOrEmpty(highwayId))
            HttpContext.Session.SetString("HighwayId", highwayId);

        bool hasAll3 = !string.IsNullOrEmpty(highwayId)
                    && !string.IsNullOrEmpty(zoneId)
                    && !string.IsNullOrEmpty(serverId);
        if (!hasAll3)
        {
            TraceLogger.Info("AirScene", nameof(GetAnimationData),
                $"Incomplete: hw={highwayId} z={zoneId} s={serverId} — returning zones/servers only");
            if (string.IsNullOrEmpty(highwayId))
                return Json(new { highwayCoords = Array.Empty<object>(), vehicles = Array.Empty<object>(),
                    bounds = (object?)null, isEW = true, servers = Array.Empty<object>() });

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
            var isEwOnly = !highwayId.Contains("I35") && !highwayId.Contains("I45") && !highwayId.Contains("I25");

            return Json(new {
                highwayCoords = zonesOnly, vehicles = Array.Empty<object>(),
                servers = serversOnly, isEW = isEwOnly,
                bridgePath = Array.Empty<object>(), hwBearing = 90.0
            });
        }

        // 1. Fetch ALL zones for this highway
        var zones = await _db.MergeZones.AsNoTracking()
            .Where(z => z.HighwayId == highwayId && z.Latitude.HasValue && z.Longitude.HasValue)
            .OrderBy(z => z.Longitude)
            .Select(z => new {
                zoneId = z.ZoneId, zoneName = z.ZoneName,
                lat = z.Latitude!.Value, lon = z.Longitude!.Value,
                highwayId = z.HighwayId, radius = z.GeofenceRadius, status = z.Status
            })
            .ToListAsync();

        if (zones.Count == 0)
            return Json(new { highwayCoords = Array.Empty<object>(), vehicles = Array.Empty<object>(),
                bounds = (object?)null, isEW = true, servers = Array.Empty<object>() });

        bool isEW = !highwayId.Contains("I35") && !highwayId.Contains("I45") && !highwayId.Contains("I25");

        var lats = zones.Select(z => z.lat).ToList();
        var lons = zones.Select(z => z.lon).ToList();
        double minLat = lats.Min(), maxLat = lats.Max();
        double minLon = lons.Min(), maxLon = lons.Max();

        if (!string.IsNullOrEmpty(zoneId))
        {
            var selZone = zones.FirstOrDefault(z => z.zoneId == zoneId);
            if (selZone != null)
            {
                double bufAlong = 0.045, bufCross = 0.004;
                if (isEW) { minLat = selZone.lat - bufCross; maxLat = selZone.lat + bufCross;
                            minLon = selZone.lon - bufAlong; maxLon = selZone.lon + bufAlong; }
                else      { minLat = selZone.lat - bufAlong; maxLat = selZone.lat + bufAlong;
                            minLon = selZone.lon - bufCross; maxLon = selZone.lon + bufCross; }
            }
        }

        // 2. Fetch AIR vehicle events
        var baseQuery = _db.VehicleEvents.AsNoTracking()
            .Where(e => e.HighwayId == highwayId
                     && (e.VehicleMode == "air" || e.IsAirFlyCar == "Y"));

        if (simOnly)
        {
            var cutoff = DateTime.UtcNow.AddMinutes(-10);
            baseQuery = baseQuery.Where(e => e.CreatedDate >= cutoff
                              && e.VehicleId != null
                              && e.VehicleId.StartsWith("SIM-"));
        }

        if (!string.IsNullOrEmpty(zoneId))
            baseQuery = baseQuery.Where(e => e.ZoneId == zoneId);

        var rawEvents = await baseQuery
            .OrderByDescending(e => e.CreatedDate)
            .Take(100)
            .Select(e => new {
                e.Id, e.VehicleId, e.EventType, e.ZoneId,
                e.SpeedMph, e.Latitude, e.Longitude, e.AltitudeMeters,
                e.VehicleMode, e.VehicleCategory, e.VehicleClassJson,
                e.IsAirFlyCar, e.CreatedDate, e.HighwayId, e.Payload
            })
            .ToListAsync();

        // 3. Prepare validated vehicles
        var validatedVehicles = new List<object>();
        foreach (var ev in rawEvents)
        {
            double lat = ev.Latitude ?? 0;
            double lon = ev.Longitude ?? 0;
            double speedMph = ev.SpeedMph ?? 0;
            double altM = ev.AltitudeMeters ?? 60;

            if (lat == 0 && lon == 0)
            {
                var nearestZone = zones.OrderBy(z => Math.Sqrt(Math.Pow(z.lat - zones.First().lat, 2) + Math.Pow(z.lon - zones.First().lon, 2))).First();
                var rng = Random.Shared;
                lat = nearestZone.lat + (rng.NextDouble() - 0.5) * 0.01;
                lon = nearestZone.lon + (rng.NextDouble() - 0.5) * 0.01;
            }

            int direction = _extractDirectionFromPayload(ev.Payload, isEW);

            validatedVehicles.Add(new
            {
                id = ev.Id,
                vehicle_id = ev.VehicleId,
                zone_id = ev.ZoneId ?? "",
                highway_id = ev.HighwayId ?? highwayId,
                speed_mph = speedMph,
                latitude = Math.Round(lat, 6),
                longitude = Math.Round(lon, 6),
                altitude_meters = altM,
                direction = direction,
                vehicle_mode = ev.VehicleMode ?? "air",
                vehicle_category = ev.VehicleCategory ?? "",
                vehicle_class_json = ev.VehicleClassJson ?? "",
                vehicle_make = _extractMakeFromPayload(ev.Payload),
                is_air_fly_car = ev.IsAirFlyCar ?? "N",
                created_date = ev.CreatedDate,
                validated = true
            });
        }

        // 4. Sort zones and compute bearing
        var sortedZones = isEW
            ? zones.OrderBy(z => z.lon).ToList()
            : zones.OrderBy(z => z.lat).ToList();

        double hwBearing = 90.0;
        if (sortedZones.Count >= 2)
        {
            var zFirst = sortedZones[0]; var zLast = sortedZones[^1];
            double mid = (zFirst.lat + zLast.lat) / 2.0;
            double cosLat = Math.Cos(mid * Math.PI / 180.0);
            double dLat = zLast.lat - zFirst.lat;
            double dLon = zLast.lon - zFirst.lon;
            hwBearing = Math.Atan2(dLon * cosLat, dLat) * 180.0 / Math.PI;
            if (hwBearing < 0) hwBearing += 360;
        }

        // 5. Build dense bridge path
        var bridgePath = new List<object>();
        const int extSteps = 10;
        const double extDeg = 0.027;
        const double interpStep = 0.003;

        if (sortedZones.Count >= 2)
        {
            var z0 = sortedZones[0]; var z1 = sortedZones[1];
            double eDlat = z0.lat - z1.lat, eDlon = z0.lon - z1.lon;
            double eLen = Math.Sqrt(eDlat*eDlat + eDlon*eDlon);
            if (eLen > 0) { eDlat /= eLen; eDlon /= eLen; }
            for (int ei = extSteps; ei >= 1; ei--) {
                double t = extDeg * ei / extSteps;
                bridgePath.Add(new { lat = Math.Round(z0.lat + eDlat*t, 6), lon = Math.Round(z0.lon + eDlon*t, 6) });
            }
            for (int si = 0; si < sortedZones.Count - 1; si++) {
                var zA = sortedZones[si]; var zB = sortedZones[si + 1];
                double segLat = zB.lat - zA.lat, segLon = zB.lon - zA.lon;
                double segLen = Math.Sqrt(segLat*segLat + segLon*segLon);
                int steps = Math.Max(3, (int)(segLen / interpStep));
                for (int st = 0; st < steps; st++) {
                    double t = (double)st / steps;
                    bridgePath.Add(new { lat = Math.Round(zA.lat + segLat*t, 6), lon = Math.Round(zA.lon + segLon*t, 6) });
                }
            }
            bridgePath.Add(new { lat = Math.Round(sortedZones[^1].lat, 6), lon = Math.Round(sortedZones[^1].lon, 6) });
            var zN1 = sortedZones[^1]; var zN2 = sortedZones[^2];
            double lDlat = zN1.lat - zN2.lat, lDlon = zN1.lon - zN2.lon;
            double lLen = Math.Sqrt(lDlat*lDlat + lDlon*lDlon);
            if (lLen > 0) { lDlat /= lLen; lDlon /= lLen; }
            for (int li = 1; li <= extSteps; li++) {
                double t = extDeg * li / extSteps;
                bridgePath.Add(new { lat = Math.Round(zN1.lat + lDlat*t, 6), lon = Math.Round(zN1.lon + lDlon*t, 6) });
            }
        }

        // 6. Fetch servers
        var zoneIds = zones.Select(z => z.zoneId).ToList();
        var servers = await _db.SwitchServers.AsNoTracking()
            .Where(s => s.ZoneId != null && zoneIds.Contains(s.ZoneId))
            .OrderBy(s => s.ZoneId).ThenBy(s => s.ServerName)
            .Select(s => new { serverId = s.ServerId, serverName = s.ServerName, zoneId = s.ZoneId ?? "" })
            .ToListAsync();

        TraceLogger.Info("AirScene", nameof(GetAnimationData),
            $"bridgePath={bridgePath.Count} pts hwBearing={hwBearing:F1}° isEW={isEW} vehicles={validatedVehicles.Count}");

        return Json(new
        {
            highwayCoords    = sortedZones,
            bridgePath       = bridgePath,
            hwBearing        = Math.Round(hwBearing, 2),
            vehicles         = validatedVehicles,
            bounds           = new { minLat, maxLat, minLon, maxLon, isEW },
            isEW             = isEW,
            highwayId        = highwayId,
            selectedZoneId   = zoneId ?? "",
            selectedServerId = serverId ?? "",
            servers          = servers,
            generatedAt      = DateTime.UtcNow
        });

        // ── Helpers ──
        static string _extractMakeFromPayload(string? payloadJson)
        {
            if (string.IsNullOrEmpty(payloadJson)) return "";
            try {
                using var doc = JsonDocument.Parse(payloadJson);
                if (doc.RootElement.TryGetProperty("vehicle_make", out var mk))
                    return mk.GetString() ?? "";
            } catch { }
            return "";
        }

        static int _extractDirectionFromPayload(string? payloadJson, bool isEW)
        {
            if (!string.IsNullOrEmpty(payloadJson)) {
                try {
                    using var doc = JsonDocument.Parse(payloadJson);
                    if (doc.RootElement.TryGetProperty("direction", out var dirEl) && dirEl.ValueKind == JsonValueKind.Number)
                        return dirEl.GetInt32();
                    if (doc.RootElement.TryGetProperty("heading", out var headEl) && headEl.ValueKind == JsonValueKind.Number)
                        return headEl.GetInt32();
                } catch { }
            }
            return isEW ? 90 : 0;
        }
    }
}
