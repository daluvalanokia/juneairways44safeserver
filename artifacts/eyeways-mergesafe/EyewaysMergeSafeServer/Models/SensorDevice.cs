using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EyewaysMergeSafeServer.Models;

[Table("SensorDevices")]
public class SensorDevice
{
    [Key] public int Id { get; set; }
    [Required, MaxLength(100)] public string DeviceName { get; set; } = "";
    [Required, MaxLength(50)]  public string DeviceId { get; set; } = "";
    [MaxLength(50)]  public string DeviceType { get; set; } = "camera";
    [MaxLength(50)]  public string? ZoneId { get; set; }
    [MaxLength(50)]  public string HighwayId { get; set; } = "";
    public double? MileMarker { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    [MaxLength(30)]  public string Status { get; set; } = "online";
    [MaxLength(20)]  public string? FirmwareVersion { get; set; }
    public DateTime LastHeartbeat { get; set; } = DateTime.UtcNow;
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
}
