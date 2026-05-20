using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EyewaysMergeSafeServer.Models;

[Table("UserProfiles")]
public class UserProfile
{
    [Key] public int Id { get; set; }
    [MaxLength(50)]  public string? UserId { get; set; }
    [Required, MaxLength(100)] public string FullName { get; set; } = "";
    [MaxLength(30)]  public string UserType { get; set; } = "viewer";
    [MaxLength(20)]  public string? Phone { get; set; }
    [MaxLength(200)] public string? Address { get; set; }
    [MaxLength(50)]  public string? HighwayId { get; set; }
    [MaxLength(100)] public string? HighwayName { get; set; }
    [MaxLength(500)] public string? DeviceIdsRaw { get; set; }
    [MaxLength(300)] public string? Notes { get; set; }
    [MaxLength(100)] public string? Password { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
}
