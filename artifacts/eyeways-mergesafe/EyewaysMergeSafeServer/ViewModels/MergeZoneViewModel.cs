using EyewaysMergeSafeServer.Models;

namespace EyewaysMergeSafeServer.ViewModels;

public class MergeZoneViewModel
{
    public List<Highway> Highways { get; set; } = new();
    public string? SelectedHighwayId { get; set; }
    public List<MergeZone> Zones { get; set; } = new();
}
