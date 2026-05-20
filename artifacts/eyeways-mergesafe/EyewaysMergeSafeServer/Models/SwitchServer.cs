using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EyewaysMergeSafeServer.Models;

[Table("SwitchServers")]
public class SwitchServer
{
    [Key] public int Id { get; set; }
    [Required, MaxLength(100)] public string ServerName { get; set; } = "";
    [Required, MaxLength(50)]  public string ServerId { get; set; } = "";
    [MaxLength(50)]  public string? ZoneId { get; set; }
    [MaxLength(50)]  public string HighwayId { get; set; } = "";
    [MaxLength(45)]  public string? IpAddress { get; set; }
    public int Port { get; set; } = 8080;
    [MaxLength(30)]  public string Status { get; set; } = "online";
    [MaxLength(20)]  public string? FirmwareVersion { get; set; }
    public long UptimeSeconds { get; set; } = 0;
    public double CpuPercent { get; set; } = 0;
    public double MemoryPercent { get; set; } = 0;
    public DateTime LastHeartbeat { get; set; } = DateTime.UtcNow;
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
}
