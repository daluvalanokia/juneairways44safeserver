using AirwaysMergeSafeServer.Models;

namespace AirwaysMergeSafeServer.ViewModels;

public class SwitchServerViewModel
{
    public List<Highway> Highways { get; set; } = new();
    public string? SelectedHighwayId { get; set; }
    public List<SwitchServer> Servers { get; set; } = new();
    public double? FilterAltitudeMin { get; set; }
    public double? FilterAltitudeMax { get; set; }
}
