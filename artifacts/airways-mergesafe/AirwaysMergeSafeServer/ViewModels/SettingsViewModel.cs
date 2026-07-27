using AirwaysMergeSafeServer.Models;

namespace AirwaysMergeSafeServer.ViewModels;

public class SettingsViewModel
{
    public List<Highway> Highways           { get; set; } = new();
    public string?       TomTomApiKey       { get; set; }
    public int           PurgeMaxDays       { get; set; } = 30;
    public int           PurgeMaxCount      { get; set; } = 10000;
    public string        AirSceneAlertsJson { get; set; } = "{}";
    public bool          InAppTraceEnabled  { get; set; } = false;
    public string        InAppTraceLevel    { get; set; } = "info";
}
