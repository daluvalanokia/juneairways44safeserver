using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AirwaysMergeSafeServer.Models;

[Table("MergeZones")]
public class MergeZone
{
    [Key] public int Id { get; set; }
    [Required, MaxLength(100)] public string ZoneName { get; set; } = "";
    [Required, MaxLength(50)]  public string ZoneId { get; set; } = "";
    [MaxLength(50)]  public string HighwayId { get; set; } = "";
    public double? MileMarker { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public int GeofenceRadius { get; set; } = 500;
    public double? AltitudeMeters { get; set; }
    [MaxLength(30)] public string Status { get; set; } = "active";
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
}
