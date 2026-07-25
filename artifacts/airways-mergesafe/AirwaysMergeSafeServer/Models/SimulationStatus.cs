using System.ComponentModel.DataAnnotations;

namespace AirwaysMergeSafeServer.Models;

/// <summary>
/// Single-row table that tracks the persistent simulation state across
/// app restarts and browser sessions.  The client writes IsRunning=true
/// with a heartbeat on every SimulationPost tick; Program.cs checks on
/// startup whether the heartbeat is stale and cleans up if needed.
/// </summary>
public class SimulationStatus
{
    public int     Id            { get; set; }
    public bool    IsRunning     { get; set; }
    public string? HighwayId     { get; set; }
    public string? ZoneId        { get; set; }
    public string? ServerId     { get; set; }
    public string? SourceType    { get; set; }
    public int     TotalPosted   { get; set; }
    public DateTime LastHeartbeat { get; set; }  // UTC — updated on every tick
    public DateTime? StoppedAt   { get; set; }    // UTC — set when sim is stopped
}
