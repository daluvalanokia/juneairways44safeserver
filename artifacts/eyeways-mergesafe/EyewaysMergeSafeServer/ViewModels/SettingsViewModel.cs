using EyewaysMergeSafeServer.Models;

namespace EyewaysMergeSafeServer.ViewModels;

public class SettingsViewModel
{
    public List<Highway> Highways { get; set; } = new();
    public string? TomTomApiKey { get; set; }
}
