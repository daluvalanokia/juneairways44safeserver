using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AirwaysMergeSafeServer.Models;

/// <summary>
/// Phase 6: Added VehicleClass (JSON), VehicleMode, and VehicleCategory columns.
///   VehicleMode     — "ground" | "air"  (fast filter for scene rendering)
///   VehicleCategory — sedan|suv|truck|motorcycle|van|air_urban|air_express
///   VehicleClass    — full JSON blob from VehicleClassifier (colour, shape, confidence …)
/// AltitudeMeters added in Phase 4 (preserved).
/// </summary>
[Table("VehicleEvents")]
public class VehicleEvent
{
    [Key] public int Id { get; set; }
    [MaxLength(30)]  public string  EventType       { get; set; } = "detection";
    [MaxLength(50)]  public string? ZoneId          { get; set; }
    [MaxLength(50)]  public string  HighwayId       { get; set; } = "";
    [MaxLength(50)]  public string? DeviceId        { get; set; }
    [MaxLength(50)]  public string? VehicleId       { get; set; }
    public double?   SpeedMph       { get; set; }
    public double?   Latitude       { get; set; }
    public double?   Longitude      { get; set; }
    public double?   AltitudeMeters { get; set; }     // Phase 4

    // ── Phase 6: Classification ────────────────────────────────────────────
    /// <summary>"ground" | "air" — fast domain filter</summary>
    [MaxLength(10)]  public string  VehicleMode     { get; set; } = "ground";
    /// <summary>sedan|suv|truck|motorcycle|van|air_urban|air_express</summary>
    [MaxLength(20)]  public string  VehicleCategory { get; set; } = "sedan";
    /// <summary>Full VehicleClass JSON — colour, shape, confidence, dimensions</summary>
    [MaxLength(800)] public string? VehicleClassJson { get; set; }

    [MaxLength(500)] public string? Payload         { get; set; }
    public DateTime  CreatedDate    { get; set; } = DateTime.UtcNow;
}
