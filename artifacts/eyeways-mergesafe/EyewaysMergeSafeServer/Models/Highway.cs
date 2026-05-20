using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EyewaysMergeSafeServer.Models;

[Table("Highways")]
public class Highway
{
    [Key] public int Id { get; set; }
    [Required, MaxLength(100)] public string Name { get; set; } = "";
    [Required, MaxLength(50)]  public string HighwayId { get; set; } = "";
    [MaxLength(50)]  public string? State { get; set; }
    [MaxLength(300)] public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
}
