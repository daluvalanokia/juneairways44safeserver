namespace AirwaysMergeSafeServer.Models;

// ── D1 FIX: Enums are now the authoritative source for all status/type values.
//    EF Core string converters in AppDbContext map these to lowercase strings
//    in the database for readability and backward compatibility.
//    Models still store the enum type; views use .ToString().ToLower() for display.

public enum ZoneStatus
{
    Active,
    Inactive,
    Fault,
    Maintenance
}

public enum ServerStatus
{
    Online,
    Offline,
    Degraded,
    Fault
}

public enum SensorStatus
{
    Online,
    Offline,
    Fault,
    Calibrating,
    Maintenance
}

public enum EventType
{
    Detection,
    Merge,
    Conflict,
    Speeding,
    Fault
}

public enum UserRole
{
    Admin,
    Operator,
    Viewer
}

public enum SourceType
{
    Physical,
    Satellite,
    Telecom,
    Tracker
}

// ── D5 FIX: IVehicleRegistry — DI interface for VehicleRegistry ─────────────
// Allows VehiclesController to receive the registry via DI rather than
// calling the static class directly, making the controller testable.
public interface IVehicleRegistry
{
    IReadOnlyList<AirwaysMergeSafeServer.Services.VehicleSpec> All { get; }
}
