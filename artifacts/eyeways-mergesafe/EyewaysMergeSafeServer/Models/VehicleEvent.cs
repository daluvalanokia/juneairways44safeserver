using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EyewaysMergeSafeServer.Models;

[Table("VehicleEvents")]
public class VehicleEvent
{
    [Key] public int Id { get; set; }
    [MaxLength(30)] public string EventType { get; set; } = "detection";
    [MaxLength(50)] public string? ZoneId { get; set; }
    [MaxLength(50)] public string HighwayId { get; set; } = "";
    [MaxLength(50)] public string? DeviceId { get; set; }
    [MaxLength(50)] public string? VehicleId { get; set; }
    public double? SpeedMph { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    [MaxLength(500)] public string? Payload { get; set; }
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
}
