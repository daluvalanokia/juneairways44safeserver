using EyewaysMergeSafeServer.Models;

namespace EyewaysMergeSafeServer.ViewModels;

public class UsersViewModel
{
    public List<Highway> Highways { get; set; } = new();
    public List<UserProfile> Users { get; set; } = new();
    public List<SensorDevice> Devices { get; set; } = new();
    public string? Search { get; set; }
}
