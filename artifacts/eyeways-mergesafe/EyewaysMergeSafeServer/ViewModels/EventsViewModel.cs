using EyewaysMergeSafeServer.Models;

namespace EyewaysMergeSafeServer.ViewModels;

public class EventsViewModel
{
    public List<Highway> Highways { get; set; } = new();
    public string? SelectedHighwayId { get; set; }
    public string FilterType { get; set; } = "all";
    public List<VehicleEvent> Events { get; set; } = new();
}
