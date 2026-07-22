using AirwaysMergeSafeServer.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AirwaysMergeSafeServer.Data;

/// <summary>
/// Fully idempotent seed — each entity block is guarded independently.
/// SeedAsync() replaces the old synchronous Seed() — safe to call on every startup.
/// If a table is empty it inserts; if rows exist it skips that block.
/// </summary>
public static class DbInitializer
{
    // ── Legacy sync wrapper (kept for any callers not yet migrated) ───────────
    public static void Seed(AppDbContext db)
        => SeedAsync(db, null).GetAwaiter().GetResult();

    public static async Task SeedAsync(AppDbContext db, ILogger? logger)
    {
        // Always upgrade plaintext passwords first (safe on every run)
        await HashExistingPasswordsAsync(db);

        // ── Highways ──────────────────────────────────────────────────────────
        if (!await db.Highways.AnyAsync())
        {
            logger?.LogInformation("Seed: inserting Highways");
            var now = DateTime.UtcNow;
            db.Highways.AddRange(
                new Highway { Name = "Interstate 20 — Texas", HighwayId = "I20-TX", State = "Texas", Description = "East-West corridor through Dallas/Fort Worth",    IsActive = true, CreatedDate = now },
                new Highway { Name = "Interstate 35 — Texas", HighwayId = "I35-TX", State = "Texas", Description = "North-South corridor through Austin/San Antonio", IsActive = true, CreatedDate = now },
                new Highway { Name = "Interstate 10 — Texas", HighwayId = "I10-TX", State = "Texas", Description = "Gulf Coast corridor through Houston to El Paso",   IsActive = true, CreatedDate = now },
                new Highway { Name = "Interstate 45 — Texas", HighwayId = "I45-TX", State = "Texas", Description = "Houston to Dallas North-South freeway",           IsActive = true, CreatedDate = now }
            );
            await db.SaveChangesAsync();
        }

        // ── MergeZones ────────────────────────────────────────────────────────
        if (!await db.MergeZones.AnyAsync())
        {
            logger?.LogInformation("Seed: inserting MergeZones");
            var now = DateTime.UtcNow;
            db.MergeZones.AddRange(
                new MergeZone { ZoneName = "I20 Dallas West Merge",      ZoneId = "I20-Z001", HighwayId = "I20-TX", MileMarker = 458.2, Latitude = 32.7767, Longitude = -96.9870, GeofenceRadius = 600, Status = "active",      CreatedDate = now },
                new MergeZone { ZoneName = "I20 Grand Prairie Exchange", ZoneId = "I20-Z002", HighwayId = "I20-TX", MileMarker = 444.5, Latitude = 32.7462, Longitude = -97.0207, GeofenceRadius = 500, Status = "active",      CreatedDate = now },
                new MergeZone { ZoneName = "I20 Arlington Merge",        ZoneId = "I20-Z003", HighwayId = "I20-TX", MileMarker = 436.1, Latitude = 32.7357, Longitude = -97.1081, GeofenceRadius = 450, Status = "fault",       CreatedDate = now },
                new MergeZone { ZoneName = "I35 Waco North Merge",       ZoneId = "I35-Z001", HighwayId = "I35-TX", MileMarker = 330.8, Latitude = 31.5493, Longitude = -97.1467, GeofenceRadius = 550, Status = "active",      CreatedDate = now },
                new MergeZone { ZoneName = "I35 Temple Bypass Zone",     ZoneId = "I35-Z002", HighwayId = "I35-TX", MileMarker = 304.2, Latitude = 31.0982, Longitude = -97.3428, GeofenceRadius = 500, Status = "maintenance", CreatedDate = now },
                new MergeZone { ZoneName = "I35 Georgetown Diverge",     ZoneId = "I35-Z003", HighwayId = "I35-TX", MileMarker = 261.5, Latitude = 30.6328, Longitude = -97.6775, GeofenceRadius = 480, Status = "active",      CreatedDate = now },
                new MergeZone { ZoneName = "I10 Houston West Merge",     ZoneId = "I10-Z001", HighwayId = "I10-TX", MileMarker = 758.1, Latitude = 29.7604, Longitude = -95.5144, GeofenceRadius = 600, Status = "active",      CreatedDate = now },
                new MergeZone { ZoneName = "I10 Katy Freeway Merge",     ZoneId = "I10-Z002", HighwayId = "I10-TX", MileMarker = 741.3, Latitude = 29.7855, Longitude = -95.7560, GeofenceRadius = 520, Status = "active",      CreatedDate = now },
                new MergeZone { ZoneName = "I10 Beaumont Approach",      ZoneId = "I10-Z003", HighwayId = "I10-TX", MileMarker = 859.2, Latitude = 30.0860, Longitude = -94.1018, GeofenceRadius = 470, Status = "inactive",    CreatedDate = now },
                new MergeZone { ZoneName = "I45 Houston North Merge",    ZoneId = "I45-Z001", HighwayId = "I45-TX", MileMarker =  52.5, Latitude = 29.9511, Longitude = -95.3677, GeofenceRadius = 550, Status = "active",      CreatedDate = now },
                new MergeZone { ZoneName = "I45 Conroe Junction",        ZoneId = "I45-Z002", HighwayId = "I45-TX", MileMarker =  85.1, Latitude = 30.3119, Longitude = -95.4561, GeofenceRadius = 500, Status = "active",      CreatedDate = now },
                new MergeZone { ZoneName = "I45 Huntsville Interchange", ZoneId = "I45-Z003", HighwayId = "I45-TX", MileMarker = 116.8, Latitude = 30.7235, Longitude = -95.5507, GeofenceRadius = 490, Status = "fault",       CreatedDate = now }
            );
            await db.SaveChangesAsync();
        }

        // ── SwitchServers ─────────────────────────────────────────────────────
        if (!await db.SwitchServers.AnyAsync())
        {
            logger?.LogInformation("Seed: inserting SwitchServers");
            var now      = DateTime.UtcNow;
            var rng      = new Random(42);
            var zones    = await db.MergeZones.ToListAsync();
            var statuses = new[] { "online", "online", "online", "degraded", "offline", "fault" };
            var servers  = new List<SwitchServer>();
            int svIdx = 1;
            foreach (var z in zones)
            {
                var (minAlt, maxAlt, width) = z.HighwayId switch
                {
                    "I20-TX" => (50.0,  150.0, 30.0),
                    "I35-TX" => (200.0, 400.0, 60.0),
                    "I10-TX" => (100.0, 250.0, 45.0),
                    "I45-TX" => (75.0,  175.0, 35.0),
                    _        => (50.0,  150.0, 30.0),
                };
                for (int i = 1; i <= 3; i++)
                {
                    double j = rng.Next(-8, 9);
                    servers.Add(new SwitchServer
                    {
                        ServerName          = $"{z.ZoneId} Switch {(char)('A' + i - 1)}",
                        ServerId            = $"SRV-{svIdx:D4}",
                        ZoneId              = z.ZoneId,
                        HighwayId           = z.HighwayId,
                        IpAddress           = $"10.{rng.Next(1,5)}.{rng.Next(1,50)}.{rng.Next(10,200)}",
                        Port                = 8080 + i,
                        Status              = statuses[rng.Next(statuses.Length)],
                        FirmwareVersion     = $"v3.{rng.Next(0,4)}.{rng.Next(0,20)}",
                        UptimeSeconds       = rng.Next(3600, 864000),
                        CpuPercent          = Math.Round(rng.NextDouble() * 80 + 5, 1),
                        MemoryPercent       = Math.Round(rng.NextDouble() * 70 + 10, 1),
                        LastHeartbeat       = now.AddMinutes(-rng.Next(0, 15)),
                        AltitudeMinMeters   = minAlt + j,
                        AltitudeMaxMeters   = maxAlt + j,
                        AltitudeWidthMeters = width  + rng.Next(-3, 4),
                        CreatedDate         = now,
                    });
                    svIdx++;
                }
            }
            db.SwitchServers.AddRange(servers);
            await db.SaveChangesAsync();
        }

        // ── SensorDevices ─────────────────────────────────────────────────────
        if (!await db.SensorDevices.AnyAsync())
        {
            logger?.LogInformation("Seed: inserting SensorDevices");
            var now         = DateTime.UtcNow;
            var rng         = new Random(42);
            var zones       = await db.MergeZones.ToListAsync();
            var deviceTypes = new[] { "camera", "lidar", "radar", "vehicle tag reader" };
            var sensors     = new List<SensorDevice>();
            int dIdx = 1;
            foreach (var z in zones)
            {
                for (int i = 0; i < 4; i++)
                {
                    var dtype = deviceTypes[i % deviceTypes.Length];
                    double alt = dtype switch
                    {
                        "lidar"              => rng.Next(18, 38),
                        "radar"              => rng.Next(22, 45),
                        "camera"             => rng.Next(8,  20),
                        "vehicle tag reader" => rng.Next(3,  10),
                        _                    => rng.Next(5,  15),
                    };
                    sensors.Add(new SensorDevice
                    {
                        DeviceName      = $"{char.ToUpper(dtype[0])}{dtype[1..]} {z.ZoneId}-{i + 1}",
                        DeviceId        = $"DEV-{dIdx:D4}",
                        DeviceType      = dtype,
                        ZoneId          = z.ZoneId,
                        HighwayId       = z.HighwayId,
                        MileMarker      = z.MileMarker + rng.NextDouble() * 0.2 - 0.1,
                        Latitude        = z.Latitude.HasValue  ? z.Latitude  + rng.NextDouble() * 0.005 - 0.0025 : null,
                        Longitude       = z.Longitude.HasValue ? z.Longitude + rng.NextDouble() * 0.005 - 0.0025 : null,
                        AltitudeMeters  = alt,
                        Status          = rng.Next(10) > 1 ? "online" : (rng.Next(2) == 0 ? "offline" : "fault"),
                        FirmwareVersion = $"fw-{rng.Next(1,4)}.{rng.Next(0,15)}",
                        LastHeartbeat   = now.AddMinutes(-rng.Next(0, 30)),
                        CreatedDate     = now,
                    });
                    dIdx++;
                }
            }
            db.SensorDevices.AddRange(sensors);
            await db.SaveChangesAsync();
        }

        // ── TriangulationConfigs ──────────────────────────────────────────────
        if (!await db.TriangulationConfigs.AnyAsync())
        {
            logger?.LogInformation("Seed: inserting TriangulationConfigs");
            var now     = DateTime.UtcNow;
            var zones   = await db.MergeZones.ToListAsync();
            var servers = await db.SwitchServers.ToListAsync();
            var configs = zones.Select(z =>
            {
                var srvs = servers.Where(s => s.ZoneId == z.ZoneId).ToList();
                return new TriangulationConfig
                {
                    ZoneId = z.ZoneId, HighwayId = z.HighwayId,
                    GeofenceRadius = z.GeofenceRadius, IsActive = z.Status == "active",
                    Switch1Label = "Switch A", Switch1ServerId = srvs.Count > 0 ? srvs[0].ServerId : "",
                    Switch1Lat = z.Latitude.HasValue  ? z.Latitude  + 0.002 : null,
                    Switch1Lon = z.Longitude.HasValue ? z.Longitude - 0.003 : null,
                    Switch2Label = "Switch B", Switch2ServerId = srvs.Count > 1 ? srvs[1].ServerId : "",
                    Switch2Lat = z.Latitude.HasValue  ? z.Latitude  - 0.002 : null,
                    Switch2Lon = z.Longitude.HasValue ? z.Longitude - 0.001 : null,
                    Switch3Label = "Switch C", Switch3ServerId = srvs.Count > 2 ? srvs[2].ServerId : "",
                    Switch3Lat = z.Latitude.HasValue  ? z.Latitude  + 0.001 : null,
                    Switch3Lon = z.Longitude.HasValue ? z.Longitude + 0.003 : null,
                    CreatedDate = now,
                };
            }).ToList();
            db.TriangulationConfigs.AddRange(configs);
            await db.SaveChangesAsync();
        }

        // ── UserProfiles ──────────────────────────────────────────────────────
        if (!await db.UserProfiles.AnyAsync())
        {
            logger?.LogInformation("Seed: inserting UserProfiles");
            var now = DateTime.UtcNow;
            db.UserProfiles.AddRange(
                new UserProfile { UserId = "admin001", FullName = "System Administrator", UserType = "admin",       Phone = "214-555-0100", HighwayId = "I20-TX", HighwayName = "Interstate 20 — Texas", Password = BCrypt.Net.BCrypt.HashPassword("admin",    12), IsActive = true,  Notes = "Primary system admin",          CreatedDate = now },
                new UserProfile { UserId = "op001",    FullName = "Maria Gonzalez",        UserType = "operator",   Phone = "817-555-0210", HighwayId = "I20-TX", HighwayName = "Interstate 20 — Texas", Password = BCrypt.Net.BCrypt.HashPassword("password", 12), IsActive = true,  Notes = "Day shift operator",            CreatedDate = now },
                new UserProfile { UserId = "op002",    FullName = "James Thompson",        UserType = "operator",   Phone = "214-555-0312", HighwayId = "I35-TX", HighwayName = "Interstate 35 — Texas", Password = BCrypt.Net.BCrypt.HashPassword("password", 12), IsActive = true,  Notes = "Night shift operator",          CreatedDate = now },
                new UserProfile { UserId = "tech001",  FullName = "Carlos Rivera",         UserType = "technician", Phone = "512-555-0401", HighwayId = "I35-TX", HighwayName = "Interstate 35 — Texas", Password = BCrypt.Net.BCrypt.HashPassword("tech123",  12), IsActive = true,  Notes = "Field technician",              CreatedDate = now },
                new UserProfile { UserId = "sup001",   FullName = "Angela Kim",            UserType = "supervisor", Phone = "713-555-0550", HighwayId = "I10-TX", HighwayName = "Interstate 10 — Texas", Password = BCrypt.Net.BCrypt.HashPassword("super",    12), IsActive = true,  Notes = "Regional supervisor",           CreatedDate = now },
                new UserProfile { UserId = "view001",  FullName = "Robert Davis",          UserType = "viewer",     Phone = "832-555-0611", HighwayId = "I45-TX", HighwayName = "Interstate 45 — Texas", Password = BCrypt.Net.BCrypt.HashPassword("viewer",   12), IsActive = true,  Notes = "Read-only viewer",              CreatedDate = now },
                new UserProfile { UserId = "op003",    FullName = "Sarah Mitchell",        UserType = "operator",   Phone = "214-555-0712", HighwayId = "I20-TX", HighwayName = "Interstate 20 — Texas", Password = BCrypt.Net.BCrypt.HashPassword("password", 12), IsActive = false, Notes = "Inactive — on leave",           CreatedDate = now },
                new UserProfile { UserId = "tech002",  FullName = "Wei Zhang",             UserType = "technician", Phone = "713-555-0888", HighwayId = "I10-TX", HighwayName = "Interstate 10 — Texas", Password = BCrypt.Net.BCrypt.HashPassword("tech123",  12), IsActive = true,  Notes = "LiDAR calibration specialist", CreatedDate = now },
                new UserProfile { UserId = "view002",  FullName = "Diana Flores",          UserType = "viewer",     Phone = "512-555-0999", HighwayId = "I35-TX", HighwayName = "Interstate 35 — Texas", Password = BCrypt.Net.BCrypt.HashPassword("viewer",   12), IsActive = true,  Notes = "Observer account",              CreatedDate = now },
                new UserProfile { UserId = "admin002", FullName = "Kevin Okafor",          UserType = "admin",      Phone = "214-555-1010", HighwayId = "I45-TX", HighwayName = "Interstate 45 — Texas", Password = BCrypt.Net.BCrypt.HashPassword("admin",    12), IsActive = true,  Notes = "Backup administrator",          CreatedDate = now },
                new UserProfile { UserId = "op004",    FullName = "Patricia Nguyen",       UserType = "operator",   Phone = "713-555-1111", HighwayId = "I10-TX", HighwayName = "Interstate 10 — Texas", Password = BCrypt.Net.BCrypt.HashPassword("password", 12), IsActive = true,  Notes = "Certified V2X operator",        CreatedDate = now }
            );
            await db.SaveChangesAsync();
        }

        // ── VehicleEvents ─────────────────────────────────────────────────────
        if (!await db.VehicleEvents.AnyAsync())
        {
            logger?.LogInformation("Seed: inserting VehicleEvents");
            var now        = DateTime.UtcNow;
            var rng        = new Random(42);
            var zones      = await db.MergeZones.ToListAsync();
            var eventTypes = new[] { "detection", "merge", "conflict", "speeding", "fault" };
            var vtypes     = new[] { "sedan", "suv", "truck", "motorcycle", "van", "air_urban", "air_express" };
            var events     = new List<VehicleEvent>();
            foreach (var z in zones)
            {
                for (int e = 0; e < 14; e++)
                {
                    var etype = eventTypes[rng.Next(eventTypes.Length)];
                    var vtype = vtypes[rng.Next(vtypes.Length)];
                    bool isAir = vtype is "air_urban" or "air_express";
                    double? alt = isAir ? rng.Next(80, 420) : (rng.Next(10) > 7 ? rng.Next(5, 30) : (double?)null);
                    events.Add(new VehicleEvent
                    {
                        EventType       = etype,
                        ZoneId          = z.ZoneId,
                        HighwayId       = z.HighwayId,
                        DeviceId        = $"DEV-{rng.Next(1, 49):D4}",
                        VehicleId       = $"VEH-{rng.Next(1000, 9999)}",
                        SpeedMph        = etype == "speeding" ? rng.Next(85, 120) : rng.Next(45, 85),
                        Latitude        = z.Latitude.HasValue  ? z.Latitude  + rng.NextDouble() * 0.002 - 0.001 : null,
                        Longitude       = z.Longitude.HasValue ? z.Longitude + rng.NextDouble() * 0.002 - 0.001 : null,
                        AltitudeMeters  = alt,
                        VehicleMode     = isAir ? "air" : "ground",
                        VehicleCategory = vtype,
                        IsAirFlyCar     = vtype == "air_express" ? "Y" : "N",
                        Payload         = $"{{\"vehicle_type\":\"{vtype}\",\"lane\":{rng.Next(1,5)},\"altitude_m\":{alt ?? 0}}}",
                        CreatedDate     = now.AddMinutes(-rng.Next(0, 1440)),
                    });
                }
            }
            db.VehicleEvents.AddRange(events);
            await db.SaveChangesAsync();
        }

        // ── InputFormatConfigs ────────────────────────────────────────────────
        if (!await db.InputFormatConfigs.AnyAsync())
        {
            logger?.LogInformation("Seed: inserting InputFormatConfigs");
            var now      = DateTime.UtcNow;
            var rng      = new Random(42);
            var fields   = new[] { "vehicle_id","timestamp","speed_mph","latitude","longitude","altitude_m","direction","lane","vehicle_type","event_type" };
            var fmtTypes = new[] { "physical", "satellite", "telecom", "tracker" };
            var fmtNames = new Dictionary<string, string[]>
            {
                ["physical"]  = new[] { "Standard Loop Detector Feed", "Piezoelectric Sensor Array" },
                ["satellite"] = new[] { "GPS Satellite Feed v2",        "Differential GPS Stream" },
                ["telecom"]   = new[] { "Cellular V2X Data Feed",       "DSRC 5.9GHz Protocol" },
                ["tracker"]   = new[] { "RFID Tag Reader Stream",       "Bluetooth Proximity Feed" },
            };
            var fmts = new List<InputFormatConfig>();
            int fIdx = 1;
            foreach (var ft in fmtTypes)
                foreach (var name in fmtNames[ft])
                {
                    fmts.Add(new InputFormatConfig
                    {
                        FormatName       = name,
                        SourceId         = $"SRC-{ft[..3].ToUpper()}-{fIdx:D3}",
                        SourceType       = ft,
                        InputSource      = $"https://feeds.airways.net/{ft}/stream/{fIdx}",
                        Description      = $"{name} — standard {ft} sensor telemetry format",
                        EnabledFieldsRaw = string.Join(",", fields.Take(rng.Next(4, fields.Length))),
                        CreatedDate      = now,
                    });
                    fIdx++;
                }
            db.InputFormatConfigs.AddRange(fmts);
            await db.SaveChangesAsync();
        }

        // ── SamplePayloads ────────────────────────────────────────────────────
        if (!await db.SamplePayloads.AnyAsync())
        {
            logger?.LogInformation("Seed: inserting SamplePayloads");
            var now = DateTime.UtcNow;
            db.SamplePayloads.AddRange(
                new SamplePayload { ConfigId = 1, SourceType = "physical",  Label = "Loop Detector Sample A", Payload = "{\"vehicle_id\":\"VEH-4821\",\"timestamp\":\"2026-05-20T09:32:11Z\",\"speed_mph\":67,\"latitude\":32.7767,\"longitude\":-96.9870,\"altitude_m\":0,\"lane\":2}",                                                                              IsValid = true,  CreatedDate = now },
                new SamplePayload { ConfigId = 3, SourceType = "satellite", Label = "GPS Feed Sample A",       Payload = "{\"vehicle_id\":\"VEH-7742\",\"timestamp\":\"2026-05-20T10:11:05Z\",\"speed_mph\":72,\"latitude\":31.5493,\"longitude\":-97.1467,\"altitude_m\":118,\"direction\":180,\"vehicle_type\":\"air_urban\"}",                                IsValid = true,  CreatedDate = now },
                new SamplePayload { ConfigId = 5, SourceType = "telecom",   Label = "V2X Cellular Sample",    Payload = "{\"vehicle_id\":\"VEH-3391\",\"timestamp\":\"2026-05-20T11:44:22Z\",\"speed_mph\":55,\"altitude_m\":0,\"event_type\":\"detection\",\"lane\":1,\"direction\":270}",                                                                    IsValid = true,  CreatedDate = now },
                new SamplePayload { ConfigId = 7, SourceType = "tracker",   Label = "RFID Tag Sample (Air)",  Payload = "{\"vehicle_id\":\"AFC-8812\",\"timestamp\":\"2026-05-20T14:00:01Z\",\"latitude\":29.7604,\"longitude\":-95.5144,\"altitude_m\":285,\"speed_mph\":130,\"vehicle_type\":\"air_express\"}",                                              IsValid = true,  CreatedDate = now },
                new SamplePayload { ConfigId = 2, SourceType = "physical",  Label = "Piezo Array Sample",     Payload = "{\"vehicle_id\":\"VEH-1155\",\"timestamp\":\"2026-05-20T08:15:00Z\",\"speed_mph\":89,\"altitude_m\":0,\"vehicle_type\":\"truck\",\"lane\":3}",                                                                                        IsValid = false, CreatedDate = now },
                new SamplePayload { ConfigId = 4, SourceType = "satellite", Label = "DGPS Stream Sample",     Payload = "{\"vehicle_id\":\"VEH-6604\",\"timestamp\":\"2026-05-20T12:22:44Z\",\"speed_mph\":65,\"latitude\":30.0860,\"longitude\":-94.1018,\"altitude_m\":0,\"direction\":90}",                                                                 IsValid = true,  CreatedDate = now }
            );
            await db.SaveChangesAsync();
        }

        logger?.LogInformation("Seed: complete — Highways={H} Zones={Z} Servers={S} Users={U} Events={E}",
            await db.Highways.CountAsync(),
            await db.MergeZones.CountAsync(),
            await db.SwitchServers.CountAsync(),
            await db.UserProfiles.CountAsync(),
            await db.VehicleEvents.CountAsync());
    }

    private static async Task HashExistingPasswordsAsync(AppDbContext db)
    {
        var users = await db.UserProfiles
            .Where(u => u.Password == null || u.Password == "" || !u.Password.StartsWith("$2"))
            .ToListAsync();
        if (!users.Any()) return;
        foreach (var u in users)
            u.Password = BCrypt.Net.BCrypt.HashPassword(
                string.IsNullOrEmpty(u.Password) ? "viewer" : u.Password, 12);
        await db.SaveChangesAsync();
    }
}
