using EyewaysMergeSafeServer.Models;

namespace EyewaysMergeSafeServer.ViewModels;

public class SensorViewModel
{
    public List<Highway> Highways { get; set; } = new();
    public string? SelectedHighwayId { get; set; }
    public string FilterType { get; set; } = "all";
    public List<SensorDevice> Sensors { get; set; } = new();
}
