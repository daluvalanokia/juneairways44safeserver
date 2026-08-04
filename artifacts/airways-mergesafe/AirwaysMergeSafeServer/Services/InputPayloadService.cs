using System.Text.Json;
using AirwaysMergeSafeServer.Data;
using AirwaysMergeSafeServer.Infrastructure;
using AirwaysMergeSafeServer.Models;

namespace AirwaysMergeSafeServer.Services;

/// <summary>
/// Phase 8: Added "airflycar" source type generator.
///
/// AirFlyCar payload fields generated:
///   Core 4D position: latitude, longitude, altitude_m, heading, speed_mph, vertical_rate_fpm
///   Flight state:     vehicle_type (air_urban|air_express), flight_phase, corridor_id
///   UAM telemetry:    battery_soc, battery_temp_c, rotor_rpm, rotor_health, motor_temp_c
///   Safety fields:    conflict_flag, separation_m, corridor_deviation_m, noise_db
///   Fleet:            passenger_count, destination_pad, pilot_id, range_remaining_km
///   Identity:         vehicle_id (AFC-XXXX format), icao_address, squawk, timestamp
///
/// Altitude generated in two realistic bands:
///   air_urban   — 30–149 m  (urban air mobility corridor)
///   air_express — 151–800 m (express / inter-city corridor)
///   air_vertiport — 0–3 m   (on-ground at vertiport pad)
///
/// All other source types (physical/satellite/telecom/tracker) preserved exactly.
/// Random.Shared used throughout (A1 fix maintained).
/// </summary>
public class InputPayloadService
{
    private readonly AppDbContext _db;

    // Static counter for stable vehicle IDs — cycles 1-20 so the same
    // pool of vehicles persists across simulation ticks, allowing the
    // 3D scene to update existing vehicles instead of creating new ones.
    private static int _vehicleCounter = 1;

    // ── Persistent vehicle state — tracks each vehicle's current GPS position ──
    // Instead of generating a random GPS each tick, we advance each vehicle's
    // position based on its speed and direction. This makes vehicles move
    // continuously along the highway instead of jumping to random positions.
    private static readonly Dictionary<string, (double lat, double lon, double speed, int dir, DateTime lastUpdate)> _vehicleState = new();
    private static readonly object _stateLock = new();

    // Called by SimulationStop to reset all vehicle positions when sim ends
    public static void ResetVehicleState()
    {
        lock (_stateLock) { _vehicleState.Clear(); }
    }

    public InputPayloadService(AppDbContext db)
    {
        _db = db;
        _vehicleCounter = (_vehicleCounter % 20) + 1;
    }

    private static readonly string[] GroundTypes      = { "sedan", "suv", "truck", "motorcycle", "van" };
    private static readonly string[] AirTypes         = { "air_urban", "air_express" };
    private static readonly string[] FlightPhases     = { "climb", "cruise", "descent", "hover", "approach" };
    private static readonly string[] GroundPhases     = { "boarding", "deboarding", "charging", "ground" };
    private static readonly string[] Corridors        = { "COR-DFW-N1", "COR-DFW-S2", "COR-AUS-E1", "COR-HOU-W3", "COR-SAT-C1" };
    private static readonly string[] DestPads         = { "PAD-DFW-01", "PAD-DFW-02", "PAD-AUS-01", "PAD-HOU-03", "PAD-SAT-01" };
    private static readonly string[] RotorHealthStates= { "nominal", "nominal", "nominal", "degraded", "warning" };

    private static readonly Dictionary<string, string[]> MakesByType = new(StringComparer.OrdinalIgnoreCase)
    {
        { "sedan",       new[] { "Toyota", "Honda", "Ford", "Chevrolet", "BMW", "Mercedes" } },
        { "suv",         new[] { "Ford", "Chevrolet", "Toyota", "Honda", "Jeep", "Tesla" } },
        { "truck",       new[] { "Ford", "Chevrolet", "Ram" } },
        { "motorcycle",  new[] { "Harley-Davidson", "Honda" } },
        { "van",         new[] { "Ford", "Mercedes" } },
        { "air_urban",   new[] { "Joby", "Wisk" } },
        { "air_express", new[] { "Archer", "Joby" } },
        { "air_vertiport", new[] { "Wisk" } },
    };

    // ── Lane GPS helpers ──────────────────────────────────────────────────────
    // Road half-width offset in degrees:
    //   ~17m perpendicular = 0.000153° lat  (1°lat ≈ 111,000m)
    //   ~17m at 32.7°N     = 0.000182° lon  (1°lon ≈ 93,300m at this lat)
    private const double LaneHalfLat = 0.000155;  // half-road-width in lat degrees
    private const double LaneHalfLon = 0.000185;  // half-road-width in lon degrees
    private const double LaneJitter  = 0.000080;  // within-lane jitter (±9m)
    private const double LongJitter  = 0.025;     // along-road scatter (±2.5km)

    /// <summary>
    /// Computes the bearing angle (degrees) of this highway based on its zone pair.
    /// Uses a lookup of adjacent zone coordinates so vehicles scatter ALONG the
    /// real road vector — not axis-aligned — so they stay on the bridge polyline.
    /// </summary>
    private static double HighwayBearingDeg(string? highwayId)
    {
        // Pre-computed bearing from first zone to last zone (OSM-verified)
        // bearing = atan2(Δlon * cos(latMid), Δlat) in degrees
        var hw = (highwayId ?? "").ToUpperInvariant();
        // Computed from actual zone GPS (atan2(Δlon*cos(lat), Δlat)):
        //   I-35 TX: Z003→Z001 = 206.4° (SSW — road goes from N to S)
        //   I-45 TX: Z001→Z003 = 348.4° (NNW)
        //   I-20 TX: Z001→Z003 = 268.8° (WSW — road goes from E to W)
        //   I-10 TX: Z001→Z003 =  75.1° (ENE)
        if (hw.Contains("I35")) return 206.0;
        if (hw.Contains("I45")) return 348.0;
        if (hw.Contains("I25")) return 355.0;
        if (hw.Contains("I20")) return 269.0;
        if (hw.Contains("I10")) return  75.0;
        return 90.0;
    }

    /// <summary>
    /// Scatter a vehicle along the highway bearing vector.
    /// along = scatter ±LongJitter along the road heading
    /// cross = scatter ±LaneHalfLat perpendicular (lane separation)
    /// Returns (lat, lon) on the road, not axis-aligned.
    /// </summary>
    private static (double lat, double lon) GenerateLanePosition(
        double zoneLat, double zoneLon, string? highwayId, Random rng, double bearingOverride = -1)
    {
        double bearingDeg = bearingOverride >= 0 ? bearingOverride : HighwayBearingDeg(highwayId);
        double bearingRad = bearingDeg * Math.PI / 180.0;

        // Along-road scatter (±LongJitter)
        double along = (rng.NextDouble() - 0.5) * LongJitter * 2;

        // Cross-road scatter (lane offset ± jitter)
        double side   = rng.Next(2) == 0 ? LaneHalfLat : -LaneHalfLat;
        double jitter = (rng.NextDouble() - 0.5) * LaneJitter * 2;
        double cross  = side + jitter;

        // Unit vector along bearing (geographic)
        // dLat/dDist = cos(bearing), dLon/dDist = sin(bearing) / cos(lat)
        double cosLat = Math.Cos(zoneLat * Math.PI / 180.0);
        double dLatAlong = Math.Cos(bearingRad) * along;
        double dLonAlong = Math.Sin(bearingRad) * along / cosLat;

        // Perpendicular unit vector (bearing + 90°)
        double perpRad = bearingRad + Math.PI / 2.0;
        double dLatCross = Math.Cos(perpRad) * cross;
        double dLonCross = Math.Sin(perpRad) * cross / cosLat;

        return (
            Math.Round(zoneLat + dLatAlong + dLatCross, 6),
            Math.Round(zoneLon + dLonAlong + dLonCross, 6)
        );
    }

    private static double GenerateLaneLat(double zoneLat, double zoneLon,
        string? highwayId, Random rng, double bearingOverride = -1)
    {
        var pos = GenerateLanePosition(zoneLat, zoneLon, highwayId, rng, bearingOverride);
        _lastLanePos = pos;
        _lastLanePosValid = true;
        return pos.lat;
    }

    private static double GenerateLaneLon(double zoneLat, double zoneLon,
        string? highwayId, Random rng, double bearingOverride = -1)
    {
        if (_lastLanePosValid)
        {
            _lastLanePosValid = false;
            return _lastLanePos.lon;
        }
        return Math.Round(zoneLon + (rng.NextDouble() - 0.5) * LongJitter * 2, 6);
    }

    // ── Thread-local cache so lat/lon share the same random draw ──────────
    [ThreadStatic] private static bool _lastLanePosValid;
    [ThreadStatic] private static (double lat, double lon) _lastLanePos;

    /// <summary>
    /// Returns a direction in degrees aligned with the highway axis.
    /// E-W highways (I-20, I-10, I-30, I-40, I-80): 90° (east) or 270° (west), chosen randomly.
    /// N-S highways (I-35, I-45, I-25): 0° (north) or 180° (south), chosen randomly.
    /// Unknown highways default to E-W.
    /// </summary>
    private static int HighwayDirectionDeg(string? highwayId, Random rng)
    {
        var hw = (highwayId ?? "").ToUpperInvariant();
        bool isNS = hw.Contains("I35") || hw.Contains("I-35") ||
                    hw.Contains("I45") || hw.Contains("I-45") ||
                    hw.Contains("I25") || hw.Contains("I-25");
        if (isNS)
            return rng.Next(2) == 0 ? 0 : 180;    // north or south
        else
            return rng.Next(2) == 0 ? 90 : 270;   // east or west (default for E-W + unknown)
    }

    /// <summary>
    /// Validates and snaps a vehicle's GPS coordinates to the selected
    /// highway/zone corridor. Called before any record is returned for
    /// animation rendering.
    /// Returns (lat, lon, snapped) — snapped=true if coordinates were corrected.
    /// </summary>
    public static (double lat, double lon, bool snapped) ValidateCoordinates(
        double lat, double lon,
        double? zoneLat, double? zoneLon,
        string? highwayId,
        string? zoneId = null,
        double maxDistDeg = 0.08)
    {
        bool snapped = false;

        if (lat == 0 && lon == 0 || double.IsNaN(lat) || double.IsNaN(lon))
        {
            snapped = true;
            // Per-highway accurate GPS fallback (not downtown Dallas generic)
            lat = zoneLat ?? HighwayDefaultLat(highwayId);
            lon = zoneLon ?? HighwayDefaultLon(highwayId);
        }

        if (zoneLat.HasValue && zoneLon.HasValue)
        {
            double dist = Math.Sqrt(
                Math.Pow(lat - zoneLat.Value, 2) + Math.Pow(lon - zoneLon.Value, 2));
            if (dist > maxDistDeg)
            {
                snapped = true;
                lat = zoneLat.Value;
                lon = zoneLon.Value;
            }
        }

        if (snapped && zoneLat.HasValue && zoneLon.HasValue)
        {
            var rng = Random.Shared;
            var hw = (highwayId ?? "").ToUpperInvariant();
            bool isNS = hw.Contains("I35") || hw.Contains("I45") || hw.Contains("I25");

            if (isNS)
            {
                var _snp1 = GenerateLanePosition(zoneLat.Value, zoneLon.Value, highwayId, rng);
                lat = _snp1.lat; lon = _snp1.lon;
            }
            else
            {
                var _snp2 = GenerateLanePosition(zoneLat.Value, zoneLon.Value, highwayId, rng);
                lat = _snp2.lat; lon = _snp2.lon;
            }
        }

        return (Math.Round(lat, 6), Math.Round(lon, 6), snapped);
    }

    public string Generate(
        string               sourceType,
        IEnumerable<string>  enabledFields,
        IEnumerable<string>? customFields = null,
        double?              zoneLat      = null,
        double?              zoneLon      = null,
        double               zoneRadiusDeg = 0.015,
        string?              zoneId       = null,
        string?              highwayId    = null,
        double               highwayBearing = 90.0)
    {
        TraceLogger.Enter("InputPayloadService", nameof(Generate), $"sourceType={sourceType}");
        try
        {
        var rng    = Random.Shared;
        var obj    = new Dictionary<string, object?>();
        var fields = enabledFields.Concat(customFields ?? Enumerable.Empty<string>()).Distinct();

        if (string.Equals(sourceType, "airflycar", StringComparison.OrdinalIgnoreCase))
        {
            var _afc = GenerateAirFlyCar(rng, fields, highwayId, zoneLat, zoneLon, highwayBearing);
            TraceLogger.Exit("InputPayloadService", nameof(Generate), "airflycar");
            return _afc;
        }

        // ── Existing source types (preserved) ────────────────────────────
        bool isAirSource  = sourceType is "satellite" or "tracker";
        bool isAirVehicle = isAirSource && rng.NextDouble() < 0.30;
        string vehicleType = isAirVehicle
            ? AirTypes[rng.Next(AirTypes.Length)]
            : GroundTypes[rng.Next(GroundTypes.Length)];

        string vehicleMake = MakesByType.TryGetValue(vehicleType, out var makes) && makes.Length > 0
            ? makes[rng.Next(makes.Length)]
            : "Unknown";

        double altitudeM = isAirVehicle
            ? (vehicleType == "air_urban" ? rng.Next(30, 150) : rng.Next(151, 801))
            : Math.Round(rng.NextDouble() * 5, 1);

        // ── Pre-compute vehicle identity + continuous position ──────────────
        // Each vehicle ID (VEH-001 to VEH-020) has a persistent position that
        // advances based on speed and direction. This makes vehicles travel
        // continuously along the highway instead of jumping to random spots.
        var _vid = $"VEH-{_vehicleCounter:D3}";
        var _spd = isAirVehicle ? rng.Next(80, 180) : rng.Next(35, 75);
        var _dir = (_vehicleCounter % 2 == 1) ? (int)Math.Round(highwayBearing) : (int)Math.Round((highwayBearing + 180) % 360);
        var _zLat = zoneLat ?? HighwayDefaultLat(highwayId);
        var _zLon = zoneLon ?? HighwayDefaultLon(highwayId);

        double _lat, _lon;
        lock (_stateLock)
        {
            if (_vehicleState.TryGetValue(_vid, out var st))
            {
                // ── Advance existing vehicle position ──────────────────────────
                var dt = (DateTime.UtcNow - st.lastUpdate).TotalSeconds;
                if (dt > 300) dt = 300; // cap at 5 min
                if (dt < 0.1) dt = 0.1;  // min 100ms
                var ms = st.speed * 0.44704; // mph → m/s
                var rad = st.dir * Math.PI / 180;
                var cosLat = Math.Cos(st.lat * Math.PI / 180);
                var dLat = ms * Math.Cos(rad) * dt / 111000.0;
                var dLon = ms * Math.Sin(rad) * dt / (111000.0 * cosLat);
                _lat = st.lat + dLat;
                _lon = st.lon + dLon;

                // ── Respawn if vehicle has left the zone corridor (>5km) ────────
                var dist = Math.Sqrt(Math.Pow(_lat - _zLat, 2) + Math.Pow(_lon - _zLon, 2));
                if (dist > 0.05)
                {
                    // Vehicle reached the end of the zone — respawn at the start
                    var pos = GenerateLanePosition(_zLat, _zLon, highwayId, rng, highwayBearing);
                    _lat = pos.lat;
                    _lon = pos.lon;
                }
                _vehicleState[_vid] = (_lat, _lon, st.speed, st.dir, DateTime.UtcNow);
            }
            else
            {
                // ── New vehicle — start at a lane position near the zone ────────
                var pos = GenerateLanePosition(_zLat, _zLon, highwayId, rng, highwayBearing);
                _lat = pos.lat;
                _lon = pos.lon;
                _vehicleState[_vid] = (_lat, _lon, _spd, _dir, DateTime.UtcNow);
            }
        }

        foreach (var f in fields)
        {
            obj[f] = f switch
            {
                "vehicle_id"      => _vid,
                "timestamp"       => DateTime.UtcNow.ToString("o"),
                "speed_mph"       => _spd,
                "latitude"        => Math.Round(_lat, 6),
                "longitude"       => Math.Round(_lon, 6),
                "altitude_m"      => altitudeM,
                "altitude_ft"     => Math.Round(altitudeM * 3.28084, 1),
                "vehicle_type"    => vehicleType,
                "vehicle_make"    => vehicleMake,
                "direction"       => _dir,
                "lane"            => isAirVehicle ? rng.Next(10, 20) : rng.Next(1, 5),
                "event_type"      => new[] { "detection","merge","speeding","conflict","fault" }[rng.Next(5)],
                "zone_id"         => !string.IsNullOrEmpty(zoneId) ? zoneId : $"ZONE-{rng.Next(1, 10):D3}",
                "highway_id"      => !string.IsNullOrEmpty(highwayId) ? highwayId : "I20-TX",
                "signal_strength" => sourceType == "telecom" ? rng.Next(-80, -30) : rng.Next(-95, -40),
                "heading"         => _dir,
                "satellite_count" => rng.Next(4, 16),
                "hdop"            => Math.Round(rng.NextDouble() * 2.5, 2),
                "rsrp"            => rng.Next(-120, -70),
                "rsrq"            => rng.Next(-15, -3),
                "tag_id"          => $"TAG-{rng.Next(100000, 999999):X}",
                "read_count"      => rng.Next(1, 10),
                "isAirFlyCar"     => "N",
                _                 => $"val_{rng.Next(100, 999)}"
            };
        }

        var _json = JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true });
        TraceLogger.Exit("InputPayloadService", nameof(Generate), $"sourceType={sourceType}");
        return _json;
        }
        catch (Exception ex) { TraceLogger.Error("InputPayloadService", nameof(Generate), ex); throw; }
    }

    /// <summary>
    /// Phase 8: AirFlyCar payload generator.
    /// Produces realistic UAM telemetry with correlated fields
    /// (flight_phase drives altitude range, battery_soc drives range_remaining_km, etc.)
    /// </summary>
    private static string GenerateAirFlyCar(Random rng, IEnumerable<string> fields,
        string? highwayId = null, double? zoneLat = null, double? zoneLon = null,
        double highwayBearing = 90.0)
    {
        var obj = new Dictionary<string, object?>();

        // ── Use zone coordinates (not highway defaults) for GPS base ───────
        double baseLat = zoneLat ?? HighwayDefaultLat(highwayId);
        double baseLon = zoneLon ?? HighwayDefaultLon(highwayId);

        // ── Fresh vehicle each tick: new ID, spawn at zone coords, ─────────
        // ── data-driven direction. No stateful tracking. The client ──────
        // ── handles all movement and exit. Vehicle starts at coords in ───
        // ── this data record, moves in direction in this data record ─────
        // ── until end of bridge then removed. No back-and-forth, no repeat.
        string vehicleId = $"AFC-{rng.Next(1000, 9999)}";
        int direction = HighwayDirectionDeg(highwayId, rng);

        var spawn = GenerateLanePosition(baseLat, baseLon, highwayId, rng, highwayBearing);
        double lat = spawn.lat;
        double lon = spawn.lon;

        bool isGrounded   = rng.NextDouble() < 0.10;
        string flightPhase = isGrounded
            ? GroundPhases[rng.Next(GroundPhases.Length)]
            : FlightPhases[rng.Next(FlightPhases.Length)];

        double altM;
        string vehicleType;
        if (isGrounded)
        {
            altM        = Math.Round(rng.NextDouble() * 3, 1);
            vehicleType = "air_urban";
        }
        else
        {
            bool isExpress = rng.NextDouble() < 0.35;
            altM        = isExpress ? rng.Next(151, 801) : rng.Next(30, 150);
            vehicleType = isExpress ? "air_express" : "air_urban";
        }

        string vehicleMake = MakesByType.TryGetValue(vehicleType, out var makes) && makes.Length > 0
            ? makes[rng.Next(makes.Length)]
            : "Unknown";

        double speedMph = isGrounded ? 0 : flightPhase switch
        {
            "climb"    => rng.Next(40, 80),
            "cruise"   => rng.Next(60, 100),
            "descent"  => rng.Next(30, 70),
            "hover"    => rng.Next(0,  10),
            "approach" => rng.Next(20, 50),
            _          => rng.Next(40, 80)
        };

        double vertRateFpm = isGrounded ? 0 : flightPhase switch
        {
            "climb"    =>  rng.Next(300, 1200),
            "cruise"   =>  rng.Next(-50, 50),
            "descent"  => -rng.Next(200, 800),
            "hover"    =>  rng.Next(-20, 20),
            "approach" => -rng.Next(50, 400),
            _          =>  0
        };

        double battSoc      = vehicleType == "air_express"
            ? Math.Round(30 + rng.NextDouble() * 50, 1)
            : Math.Round(50 + rng.NextDouble() * 48, 1);

        double rangeKm      = Math.Round(battSoc * 0.8 + rng.NextDouble() * 20, 1);
        double rotorRpm     = isGrounded ? rng.Next(0, 200) : rng.Next(1800, 3200);
        double motorTempC   = isGrounded ? rng.Next(20, 40) : rng.Next(55, 110);
        double battTempC    = isGrounded ? rng.Next(20, 35) : rng.Next(30, 55);
        double noiseDb      = isGrounded ? rng.Next(40, 65) : rng.Next(60, 85);
        bool   conflictFlag = !isGrounded && rng.NextDouble() < 0.08;
        double separationM  = conflictFlag ? rng.Next(50, 300) : rng.Next(400, 2000);
        double corrDevM     = isGrounded   ? 0 : Math.Round(rng.NextDouble() * 80, 1);
        string corridorId   = Corridors[rng.Next(Corridors.Length)];
        string icao         = $"{rng.Next(0x400000, 0xFFFFFF):X6}";
        int    squawk       = rng.Next(1000, 7776);
        string pilotId      = $"PLT-{rng.Next(100, 999)}";
        string destPad      = DestPads[rng.Next(DestPads.Length)];
        string rotorHealth  = RotorHealthStates[rng.Next(RotorHealthStates.Length)];

        string eventType = conflictFlag ? "conflict"
            : flightPhase == "approach" ? "merge"
            : "detection";

        foreach (var f in fields)
        {
            obj[f] = f switch
            {
                "vehicle_id"           => vehicleId,
                "timestamp"            => DateTime.UtcNow.ToString("o"),
                "latitude"             => lat,
                "longitude"            => lon,
                "altitude_m"           => altM,
                "speed_mph"            => Math.Round(speedMph, 1),
                "heading"              => direction,
                "vehicle_type"         => vehicleType,
                "vehicle_make"         => vehicleMake,
                "flight_phase"         => flightPhase,
                "vertical_rate_fpm"    => vertRateFpm,
                "battery_soc"          => battSoc,
                "battery_temp_c"       => battTempC,
                "range_remaining_km"   => rangeKm,
                "rotor_rpm"            => rotorRpm,
                "rotor_health"         => rotorHealth,
                "motor_temp_c"         => motorTempC,
                "noise_db"             => noiseDb,
                "corridor_id"          => corridorId,
                "corridor_deviation_m" => corrDevM,
                "conflict_flag"        => conflictFlag ? 1 : 0,
                "separation_m"         => Math.Round(separationM, 1),
                "passenger_count"      => isGrounded ? rng.Next(0, 5) : rng.Next(1, 5),
                "destination_pad"      => destPad,
                "pilot_id"             => pilotId,
                "icao_address"         => icao,
                "squawk"               => squawk,
                "nic"                  => rng.Next(8, 12),
                "nac_p"                => rng.Next(8, 11),
                "zone_id"              => $"ZONE-{rng.Next(1, 10):D3}",
                "highway_id"           => highwayId ?? "I20-TX",
                "event_type"           => eventType,
                "isAirFlyCar"          => "Y",
                _                      => $"val_{rng.Next(100, 999)}"
            };
        }

        return JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// Parses altitude from a raw JSON payload string.
    /// Checks fields: altitude_m, alt_m, alt, altitude, elevation (in that order).
    /// Returns null if none found or payload is not valid JSON.
    /// </summary>
    public static double? ParseAltitude(string payloadJson)
    {
        TraceLogger.Enter("InputPayloadService", nameof(ParseAltitude));
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            TraceLogger.Exit("InputPayloadService", nameof(ParseAltitude), "null-input");
            return null;
        }
        try
        {
            using var doc  = JsonDocument.Parse(payloadJson);
            var root       = doc.RootElement;
            var candidates = new[] { "altitude_m", "alt_m", "alt", "altitude", "elevation" };
            foreach (var key in candidates)
                if (root.TryGetProperty(key, out var val) && val.TryGetDouble(out double d))
                {
                    TraceLogger.Exit("InputPayloadService", nameof(ParseAltitude), $"{d}m via '{key}'");
                    return d;
                }
        }
        catch (Exception ex) { TraceLogger.Error("InputPayloadService", nameof(ParseAltitude), ex); }
        TraceLogger.Exit("InputPayloadService", nameof(ParseAltitude), "not-found");
        return null;
    }

    public async Task<SamplePayload> GenerateAndSaveAsync(int configId)
    {
        TraceLogger.Enter("InputPayloadService", nameof(GenerateAndSaveAsync), $"configId={configId}");
        try
        {
            var config = await _db.InputFormatConfigs.FindAsync(configId)
                ?? throw new ArgumentException($"Config {configId} not found");
            var fields  = config.EnabledFieldsRaw?.Split(',', StringSplitOptions.RemoveEmptyEntries)
                          ?? Array.Empty<string>();
            var payload = Generate(config.SourceType, fields);
            var sample  = new SamplePayload
            {
                ConfigId    = configId,
                SourceType  = config.SourceType,
                Label       = $"{config.FormatName} — {DateTime.UtcNow:HH:mm:ss}",
                Payload     = payload,
                IsValid     = true,
                CreatedDate = DateTime.UtcNow
            };
            _db.SamplePayloads.Add(sample);
            await _db.SaveChangesAsync();
            TraceLogger.Exit("InputPayloadService", nameof(GenerateAndSaveAsync), $"sampleId={sample.Id}");
            return sample;
        }
        catch (Exception ex) { TraceLogger.Error("InputPayloadService", nameof(GenerateAndSaveAsync), ex); throw; }
    }

    /// <summary>Per-highway centreline latitude fallback — OSM-verified road midpoints.</summary>
    private static double HighwayDefaultLat(string? highwayId)
    {
        var hw = (highwayId ?? "").ToUpperInvariant();
        if (hw.Contains("I20")) return 32.7213;  // I-20 Grand Prairie centreline
        if (hw.Contains("I35")) return 31.0985;  // I-35 Temple midpoint
        if (hw.Contains("I10")) return 29.7855;  // I-10 Katy Freeway
        if (hw.Contains("I45")) return 30.3119;  // I-45 Conroe Junction
        return 32.7213;  // default: I-20 centreline
    }

    /// <summary>Per-highway centreline longitude fallback — OSM-verified road midpoints.</summary>
    private static double HighwayDefaultLon(string? highwayId)
    {
        var hw = (highwayId ?? "").ToUpperInvariant();
        if (hw.Contains("I20")) return -97.0207;  // I-20 Grand Prairie centreline
        if (hw.Contains("I35")) return -97.3428;  // I-35 Temple midpoint
        if (hw.Contains("I10")) return -95.7560;  // I-10 Katy Freeway
        if (hw.Contains("I45")) return -95.4561;  // I-45 Conroe Junction
        return -97.0207;  // default: I-20 centreline
    }

}
