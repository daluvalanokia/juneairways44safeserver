using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AirwaysMergeSafeServer.Models;

/// <summary>
/// E2: Audit log — one record per mutating operation across the system.
///     Written by AuditService, which is injected into every controller that
///     performs Create / Edit / Delete operations.
///
///     Table grows append-only; purged via Settings → Data Purge (same mechanism
///     as VehicleEvents purge, controlled by PurgeMaxDays).
/// </summary>
[Table("AuditLogs")]
public class AuditLog
{
    [Key] public long Id { get; set; }

    /// <summary>UserId from session (e.g. "admin", "op1")</summary>
    [MaxLength(50)]  public string  UserId     { get; set; } = "";

    /// <summary>Full name from session</summary>
    [MaxLength(100)] public string  FullName   { get; set; } = "";

    /// <summary>Highway the user was operating on</summary>
    [MaxLength(50)]  public string? HighwayId  { get; set; }

    /// <summary>Controller name (e.g. "MergeZones", "SwitchServers")</summary>
    [MaxLength(60)]  public string  Controller { get; set; } = "";

    /// <summary>Action performed: Create | Edit | Delete | Login | Logout | InjectDemo</summary>
    [MaxLength(30)]  public string  Action     { get; set; } = "";

    /// <summary>Entity type being mutated (e.g. "MergeZone", "SensorDevice")</summary>
    [MaxLength(60)]  public string? EntityType { get; set; }

    /// <summary>String representation of the entity ID (int, string, guid)</summary>
    [MaxLength(80)]  public string? EntityId   { get; set; }

    /// <summary>Short human-readable summary of what changed</summary>
    [MaxLength(500)] public string? Summary    { get; set; }

    /// <summary>Client IP address</summary>
    [MaxLength(45)]  public string? IpAddress  { get; set; }

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
}
