using EyewaysMergeSafeServer.Models;

namespace EyewaysMergeSafeServer.ViewModels;

public class SwitchServerViewModel
{
    public List<Highway> Highways { get; set; } = new();
    public string? SelectedHighwayId { get; set; }
    public List<SwitchServer> Servers { get; set; } = new();
}
