using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AirwaysMergeSafeServer.Models;

/// <summary>
/// AirCar registry entry — air vehicle specs for the 3D Air Scene.
/// Separate from VehicleEvent (which stores detection events).
/// This model is the persistent backing store for the AirCar Vehicle Registry.
/// </summary>
[Table("AirCarRegistry")]
public class AirCarEntry
{
    [Key] public int Id { get; set; }

    [MaxLength(30)]  public string Type            { get; set; } = "evtol";
    [MaxLength(50)]  public string Make            { get; set; } = "";
    [MaxLength(50)]  public string Model           { get; set; } = "";
    [MaxLength(10)]  public string Size            { get; set; } = "medium";
    [MaxLength(5)]   public string Icon            { get; set; } = "✈";
    [MaxLength(2000)] public string BrandLogo      { get; set; } = "";
    [MaxLength(2000)] public string SideViewLogo    { get; set; } = "";
    [MaxLength(500)]  public string ColorsJson     { get; set; } = "[]";

    public float LengthM        { get; set; }
    public float WidthM         { get; set; }    // Wingspan
    public float HeightM        { get; set; }
    public float MaxAltitudeM    { get; set; }
    public float CruiseSpeedMph { get; set; }

    public bool IsActive        { get; set; } = true;
    public DateTime CreatedDate  { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedDate  { get; set; } = DateTime.UtcNow;
}
