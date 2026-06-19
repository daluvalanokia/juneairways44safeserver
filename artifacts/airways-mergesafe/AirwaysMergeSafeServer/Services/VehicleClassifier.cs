using System.Text.Json;

namespace AirwaysMergeSafeServer.Services;

/// <summary>
/// Phase 8: Extended VehicleClassifier.
///
/// NEW: airflycar source type
///   - Highest trust weight (0.22) — dedicated UAM telemetry
///   - Categories: air_urban (≤150 m), air_express (>150 m), air_vertiport (on-ground ≤5 m)
///   - Additional fields parsed: flight_phase, battery_soc, corridor_id, conflict_flag,
///     vertical_rate_fpm, rotor_rpm, corridor_deviation_m
///
/// ALGORITHM ADDITIONS (Step 1b — AirFlyCar gate):
///   sourceType == "airflycar"
///     → flight_phase == "ground" | "boarding" | "deboarding"  → air_vertiport
///     → altitude_m ≤ 10                                        → air_vertiport
///     → altitude_m ≤ 150                                       → air_urban
///     → altitude_m > 150                                       → air_express
///     → (no altitude) speed_mph > 60                          → air_urban (assumed)
///
/// RENDER SPEC ADDITIONS:
///   air_vertiport → RingGeometry (pad marker)  #f0abfc   Y = 0.1
///   air_urban     → ConeGeometry (inverted)    #00bcd4   Y = altM × ALT_SCALE
///   air_express   → OctahedronGeometry         #ff6b35   Y = altM × ALT_SCALE
///
/// VIEW-SPECIFIC GEOMETRY (consumed by AirScene.cshtml JS):
///   Top view  — all air vehicles shown as circles with wingspan radius
///               air_vertiport shown as concentric rings (pad)
///   Side view — vehicles stacked on Y axis, altitude labels rendered
/// </summary>
public class VehicleClassifier
{
    private static readonly Dictionary<string, double> SourceWeights = new(StringComparer.OrdinalIgnoreCase)
    {
        { "physical",   0.20 },
        { "satellite",  0.18 },
        { "tracker",    0.16 },
        { "telecom",    0.14 },
        { "airflycar",  0.22 },   // Phase 8: highest trust — dedicated UAM telemetry
    };

    private static readonly Dictionary<string, VehicleRenderSpec> GroundSpecs = new()
    {
        { "sedan",      new("sedan",      "#3b82f6", "box",       1.6f, 0.80f, 0.40f) },
        { "suv",        new("suv",        "#22c55e", "box",       1.9f, 0.90f, 0.55f) },
        { "truck",      new("truck",      "#f59e0b", "box_cab",   2.4f, 0.95f, 0.75f) },
        { "motorcycle", new("motorcycle", "#a855f7", "cylinder",  0.6f, 0.30f, 0.50f) },
        { "van",        new("van",        "#64748b", "box",       2.1f, 1.00f, 1.00f) },
    };

    private static readonly Dictionary<string, VehicleRenderSpec> AirSpecs = new()
    {
        { "air_vertiport", new("air_vertiport", "#f0abfc", "ring",        0.8f, 10.7f, 0.10f) }, // Phase 8
        { "air_urban",     new("air_urban",     "#00bcd4", "cone_inv",    0.5f,  0.50f, 0.30f) },
        { "air_express",   new("air_express",   "#ff6b35", "octahedron",  0.6f,  0.60f, 0.60f) },
    };

    public VehicleClass Classify(string? payloadJson, string sourceType = "physical")
    {
        double? altM = null, speedMph = null, lat = null, lon = null;
        double? verticalRateFpm = null, battSoc = null, rotorRpm = null, corridorDevM = null;
        string? vehicleType = null, flightPhase = null, corridorId = null;
        bool hasVehicleType = false, hasAlt = false, hasSpeed = false, hasLatLon = false;
        bool hasFlightPhase = false, conflictFlag = false;

        if (!string.IsNullOrEmpty(payloadJson))
        {
            try
            {
                using var doc  = JsonDocument.Parse(payloadJson);
                var root = doc.RootElement;

                if (TryGetDouble(root, "altitude_m",  out var am))  { altM  = am;  hasAlt = true; }
                else if (TryGetDouble(root, "alt",    out var a2))  { altM  = a2;  hasAlt = true; }
                else if (TryGetDouble(root, "altitude_ft", out var af)) { altM = af * 0.3048; hasAlt = true; }

                if (TryGetDouble(root, "speed_mph",          out var sp))  { speedMph        = sp;   hasSpeed = true; }
                if (TryGetDouble(root, "vertical_rate_fpm",  out var vr))    verticalRateFpm = vr;
                if (TryGetDouble(root, "battery_soc",        out var bs))    battSoc         = bs;
                if (TryGetDouble(root, "rotor_rpm",          out var rr))    rotorRpm        = rr;
                if (TryGetDouble(root, "corridor_deviation_m", out var cd))  corridorDevM   = cd;

                if (TryGetDouble(root, "latitude",  out var la) &&
                    TryGetDouble(root, "longitude", out var lo))
                { lat = la; lon = lo; hasLatLon = true; }

                if (root.TryGetProperty("vehicle_type", out var vtEl))
                { vehicleType = vtEl.GetString()?.ToLowerInvariant(); hasVehicleType = !string.IsNullOrEmpty(vehicleType); }

                if (root.TryGetProperty("flight_phase", out var fpEl))
                { flightPhase = fpEl.GetString()?.ToLowerInvariant(); hasFlightPhase = !string.IsNullOrEmpty(flightPhase); }

                if (root.TryGetProperty("corridor_id", out var ciEl))
                    corridorId = ciEl.GetString();

                if (root.TryGetProperty("conflict_flag", out var cfEl))
                    conflictFlag = cfEl.ValueKind == JsonValueKind.True ||
                                   (cfEl.ValueKind == JsonValueKind.Number && cfEl.GetInt32() != 0) ||
                                   cfEl.GetString() == "true";
            }
            catch { /* non-fatal */ }
        }

        // ── Step 1b (Phase 8): AirFlyCar gate — highest priority ─────────
        bool isAirFlyCarSource = string.Equals(sourceType, "airflycar", StringComparison.OrdinalIgnoreCase);
        string domain, category;

        if (isAirFlyCarSource)
        {
            domain = "air";
            // On-ground phases override altitude
            var groundPhases = new[] { "ground", "boarding", "deboarding", "charging", "maintenance" };
            if (hasFlightPhase && flightPhase != null && groundPhases.Contains(flightPhase))
                category = "air_vertiport";
            else if (hasAlt && altM.HasValue && altM.Value <= 5)
                category = "air_vertiport";
            else if (hasAlt && altM.HasValue && altM.Value <= 150)
                category = "air_urban";
            else if (hasAlt && altM.HasValue && altM.Value > 150)
                category = "air_express";
            else if (hasSpeed && speedMph.HasValue && speedMph.Value > 60)
                category = "air_urban";       // airflycar w/o altitude assumed airborne
            else
                category = "air_urban";        // safe default for airflycar source
        }
        // ── Step 1 (existing): Standard altitude gate ─────────────────────
        else if (hasAlt && altM.HasValue && altM.Value > 10)
        {
            domain   = "air";
            category = altM.Value <= 150.0 ? "air_urban" : "air_express";
        }
        else
        {
            domain = "ground";
            if (hasVehicleType && GroundSpecs.ContainsKey(vehicleType!))
                category = vehicleType!;
            else
                category = speedMph switch
                {
                    double s when s <= 25 => "motorcycle",
                    double s when s <= 55 => "sedan",
                    double s when s <= 75 => "suv",
                    double s when s <= 90 => "truck",
                    double s when s > 90  => "van",
                    _                     => "sedan"
                };
        }

        // ── Step 4: Confidence score ──────────────────────────────────────
        double sw = SourceWeights.TryGetValue(sourceType, out var w) ? w : 0.12;
        double confidence =
              (hasVehicleType  ? 0.25 : 0.0)
            + (hasAlt          ? 0.25 : 0.0)
            + sw
            + (hasSpeed        ? 0.10 : 0.0)
            + (hasLatLon       ? 0.10 : 0.0)
            + (hasFlightPhase  ? 0.08 : 0.0);   // Phase 8: flight_phase boosts confidence

        confidence = Math.Round(Math.Min(1.0, confidence), 2);

        var spec = domain == "air"
            ? (AirSpecs.TryGetValue(category, out var as_)   ? as_ : AirSpecs["air_urban"])
            : (GroundSpecs.TryGetValue(category, out var gs) ? gs  : GroundSpecs["sedan"]);

        // Phase 8: override colour on conflict
        var color = conflictFlag ? "#ef4444" : spec.Color;

        return new VehicleClass(
            Domain:           domain,
            Category:         category,
            Color:            color,
            Shape3D:          spec.Shape,
            LengthU:          spec.L,
            WidthU:           spec.W,
            HeightU:          spec.H,
            Confidence:       confidence,
            LowConfidence:    confidence < 0.40,
            AltitudeM:        altM,
            SpeedMph:         speedMph,
            Latitude:         lat,
            Longitude:        lon,
            // Phase 8 extras
            FlightPhase:      flightPhase,
            VerticalRateFpm:  verticalRateFpm,
            BatterySoc:       battSoc,
            RotorRpm:         rotorRpm,
            CorridorId:       corridorId,
            CorridorDeviationM: corridorDevM,
            ConflictFlag:     conflictFlag,
            WingspanU:        domain == "air" ? spec.W : null
        );
    }

    private static bool TryGetDouble(JsonElement root, string key, out double value)
    {
        value = 0;
        return root.TryGetProperty(key, out var el)
            && el.ValueKind == JsonValueKind.Number
            && el.TryGetDouble(out value);
    }
}

/// <summary>
/// Phase 8: Extended with UAM-specific fields.
/// All new fields nullable — backward-compatible with existing records.
/// </summary>
public sealed record VehicleClass(
    string  Domain,
    string  Category,
    string  Color,
    string  Shape3D,
    float   LengthU,
    float   WidthU,
    float   HeightU,
    double  Confidence,
    bool    LowConfidence,
    double? AltitudeM,
    double? SpeedMph,
    double? Latitude,
    double? Longitude,
    // Phase 8: AirFlyCar-specific
    string? FlightPhase       = null,
    double? VerticalRateFpm   = null,
    double? BatterySoc        = null,
    double? RotorRpm          = null,
    string? CorridorId        = null,
    double? CorridorDeviationM = null,
    bool    ConflictFlag      = false,
    float?  WingspanU         = null
);

internal sealed record VehicleRenderSpec(
    string Category, string Color, string Shape,
    float L, float W, float H);
