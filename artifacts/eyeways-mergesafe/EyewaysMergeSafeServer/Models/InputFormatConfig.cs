using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EyewaysMergeSafeServer.Models;

[Table("InputFormatConfigs")]
public class InputFormatConfig
{
    [Key] public int Id { get; set; }
    [Required, MaxLength(100)] public string FormatName { get; set; } = "";
    [MaxLength(50)]  public string? SourceId { get; set; }
    [MaxLength(30)]  public string SourceType { get; set; } = "physical";
    [MaxLength(300)] public string? InputSource { get; set; }
    [MaxLength(300)] public string? Description { get; set; }
    [MaxLength(1000)] public string? EnabledFieldsRaw { get; set; }
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
}
