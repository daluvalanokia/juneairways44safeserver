using EyewaysMergeSafeServer.Models;

namespace EyewaysMergeSafeServer.ViewModels;

public class DashboardViewModel
{
    public List<Highway> Highways { get; set; } = new();
    public string? SelectedHighwayId { get; set; }
    public List<MergeZone> Zones { get; set; } = new();
    public List<SwitchServer> Servers { get; set; } = new();
    public List<SensorDevice> Sensors { get; set; } = new();
    public List<VehicleEvent> RecentEvents { get; set; } = new();
    public int FaultZones => Zones.Count(z => z.Status == "fault");
    public int OnlineServers => Servers.Count(s => s.Status == "online");
    public int OnlineSensors => Sensors.Count(s => s.Status == "online");
    public int AlertCount => RecentEvents.Count(e => e.EventType is "conflict" or "fault");
}
