using EyewaysMergeSafeServer.Models;

namespace EyewaysMergeSafeServer.ViewModels;

public class DataInputFormatsViewModel
{
    public List<Highway>          Highways         { get; set; } = new();
    public string?                SelectedHighwayId{ get; set; }
    public string                 ActiveTab        { get; set; } = "physical";
    public List<InputFormatConfig>PhysicalConfigs  { get; set; } = new();
    public List<InputFormatConfig>SatelliteConfigs { get; set; } = new();
    public List<InputFormatConfig>TelecomConfigs   { get; set; } = new();
    public List<InputFormatConfig>TrackerConfigs   { get; set; } = new();
    public List<SamplePayload>    SavedPayloads    { get; set; } = new();
    public List<MergeZone>        AllZones         { get; set; } = new();
    public List<SwitchServer>     AllSwitchServers { get; set; } = new();
}
