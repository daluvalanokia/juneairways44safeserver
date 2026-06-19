using AirwaysMergeSafeServer.Models;

namespace AirwaysMergeSafeServer.ViewModels;

/// <summary>
/// Phase 7: Extended with RecentEventsJson (pre-classified), ground/air counts,
/// and CategoryBreakdown for the inspector legend in both Index and AirScene.
/// </summary>
public class Traffic3DViewModel
{
    public List<Highway>      Highways          { get; set; } = new();
    public string?            SelectedHighwayId { get; set; }
    public string?            SelectedZoneId    { get; set; }
    public List<MergeZone>    Zones             { get; set; } = new();
    public List<SwitchServer> SwitchServers     { get; set; } = new();
    public List<SensorDevice> Sensors           { get; set; } = new();
    public string?            TomTomApiKey      { get; set; }

    // Phase 6/7: pre-classified events serialised to JSON for scene bootstrap
    // Shape: [{ id, vehicleId, eventType, zoneId, speedMph, latitude, longitude,
    //           altitudeMeters, vehicleMode, vehicleCategory, vehicleClassJson, createdDate }]
    public string  RecentEventsJson  { get; set; } = "[]";
    public int     GroundCount       { get; set; }
    public int     AirCount          { get; set; }

    // Category breakdown: { "sedan":12, "air_urban":3, … }
    public Dictionary<string, int> CategoryBreakdown { get; set; } = new();
}
