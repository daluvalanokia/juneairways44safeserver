using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EyewaysMergeSafeServer.Models;

[Table("TriangulationConfigs")]
public class TriangulationConfig
{
    [Key] public int Id { get; set; }
    [MaxLength(50)] public string ZoneId { get; set; } = "";
    [MaxLength(50)] public string HighwayId { get; set; } = "";
    public int GeofenceRadius { get; set; } = 500;
    public bool IsActive { get; set; } = true;
    [MaxLength(50)] public string? Switch1Label { get; set; }
    [MaxLength(50)] public string? Switch1ServerId { get; set; }
    public double? Switch1Lat { get; set; }
    public double? Switch1Lon { get; set; }
    [MaxLength(50)] public string? Switch2Label { get; set; }
    [MaxLength(50)] public string? Switch2ServerId { get; set; }
    public double? Switch2Lat { get; set; }
    public double? Switch2Lon { get; set; }
    [MaxLength(50)] public string? Switch3Label { get; set; }
    [MaxLength(50)] public string? Switch3ServerId { get; set; }
    public double? Switch3Lat { get; set; }
    public double? Switch3Lon { get; set; }
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
}
