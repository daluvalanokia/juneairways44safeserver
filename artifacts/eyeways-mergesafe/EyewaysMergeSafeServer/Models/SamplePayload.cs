using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EyewaysMergeSafeServer.Models;

[Table("SamplePayloads")]
public class SamplePayload
{
    [Key] public int Id { get; set; }
    public int? ConfigId { get; set; }
    [MaxLength(30)]  public string SourceType { get; set; } = "physical";
    [MaxLength(100)] public string? Label { get; set; }
    public string? Payload { get; set; }
    public bool IsValid { get; set; } = true;
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
}
