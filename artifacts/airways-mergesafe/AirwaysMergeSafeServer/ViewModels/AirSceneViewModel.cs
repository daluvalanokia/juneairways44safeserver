using AirwaysMergeSafeServer.Models;

namespace AirwaysMergeSafeServer.ViewModels;

/// <summary>
/// Task 10: Dedicated ViewModel for the independent AirScene controller/view.
/// Mirrors Traffic3DViewModel shape so existing AirScene JS needs minimal changes.
/// </summary>
public class AirSceneViewModel
{
    public List<Highway>      Highways          { get; set; } = new();
    public string?            SelectedHighwayId { get; set; }
    public List<MergeZone>    Zones             { get; set; } = new();
    public List<SwitchServer> SwitchServers     { get; set; } = new();
    public List<SensorDevice> Sensors           { get; set; } = new();

    // Pre-classified events serialised to JSON for scene bootstrap
    public string  RecentEventsJson  { get; set; } = "[]";
    public int     GroundCount       { get; set; }
    public int     AirCount          { get; set; }

    // Category breakdown: { "sedan":12, "air_urban":3, … }
    public Dictionary<string, int> CategoryBreakdown { get; set; } = new();

    // Air Scene speed-alert configuration (JSON blob)
    public string AirSceneAlertsJson { get; set; } = "{}";

    // AirCar registry specs (JSON): logos, colors, dimensions for 3D rendering
    public string AirCarSpecsJson { get; set; } = "[]";
}
