using EyewaysMergeSafeServer.Models;

namespace EyewaysMergeSafeServer.ViewModels;

public class Traffic3DViewModel
{
    public List<Highway> Highways { get; set; } = new();
    public string? SelectedHighwayId { get; set; }
    public List<MergeZone> Zones { get; set; } = new();
    public List<SensorDevice> Sensors { get; set; } = new();
    public string? TomTomApiKey { get; set; }
}
