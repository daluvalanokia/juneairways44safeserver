using AirwaysMergeSafeServer.Data;
using AirwaysMergeSafeServer.Filters;
using AirwaysMergeSafeServer.Infrastructure;
using AirwaysMergeSafeServer.Services;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Serilog;

// ── E6: Serilog bootstrap logger (catches startup errors before DI is ready) ─
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

// Initialise trace log before anything else — writes to /tmp/trace_{timestamp}.log
TraceLogger.Initialise();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // E6: Full Serilog logging with file + optional Seq sinks
    builder.AddSerilogLogging();

    // DEV FIX: Configure Kestrel from appsettings.Development.json "Kestrel" section.
    // This makes `dotnet run` bind to http://localhost:5000 + https://localhost:5001
    // as defined in appsettings.Development.json (and launchSettings.json).
    builder.WebHost.ConfigureKestrel((ctx, opts) =>
    {
        opts.AddServerHeader = false;
        opts.Configure(ctx.Configuration.GetSection("Kestrel"));
    });

    // TomTom key file (optional external config — A4)
    var tomTomKeyFile = Path.Combine(AppContext.BaseDirectory, "tomtomkey.json");
    if (File.Exists(tomTomKeyFile))
        builder.Configuration.AddJsonFile(tomTomKeyFile, optional: true, reloadOnChange: true);

    // ── Database (C5: no startup DDL) ─────────────────────────────────────
    var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
    if (!string.IsNullOrEmpty(databaseUrl))
        builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(ParsePostgresUrl(databaseUrl)));
    else
        builder.Services.AddDbContext<AppDbContext>(o =>
            o.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

    // ── MVC + global session-auth filter ──────────────────────────────────
    builder.Services.AddControllersWithViews(opts =>
    {
        opts.Filters.Add<SessionAuthFilter>();
        opts.Filters.Add<TraceActionFilter>(); // global entry/exit trace for all controllers
    });

    // ── Session (secure) ──────────────────────────────────────────────────
    // DEV FIX: CookieSecurePolicy.Always rejects cookies over plain HTTP (localhost:5000).
    // In Development we use SameAsRequest so both http:5000 and https:5001 work.
    // Production retains Always — HTTPS enforced by reverse-proxy / HSTS.
    var isDevelopment = builder.Environment.IsDevelopment();
    builder.Services.AddSession(options =>
    {
        options.IdleTimeout         = TimeSpan.FromHours(2);
        options.Cookie.HttpOnly     = true;
        options.Cookie.IsEssential  = true;
        options.Cookie.SecurePolicy = isDevelopment
            ? CookieSecurePolicy.SameAsRequest   // allows http://localhost:5000
            : CookieSecurePolicy.Always;          // HTTPS only in production
        options.Cookie.SameSite     = SameSiteMode.Lax;   // Strict breaks some POST redirects on HTTP
        options.Cookie.Name         = "__mss";
    });

    builder.Services.AddMemoryCache();
    builder.Services.AddOutputCache(opts =>
    {
        opts.AddPolicy("Highways",  p => p.Expire(TimeSpan.FromMinutes(10)).Tag("highways"));
        opts.AddPolicy("ShortLive", p => p.Expire(TimeSpan.FromMinutes(5)));
    });
    builder.Services.AddHttpClient();
    builder.Services.AddResponseCompression(opts => { opts.EnableForHttps = true; });

    // E3: Rate limiting — login, ingest, and API read policies
    builder.Services.AddAppRateLimiting();

    // E2: AuditService — requires IHttpContextAccessor
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<AuditService>();

    // ── App services ──────────────────────────────────────────────────────
    builder.Services.AddScoped<InputPayloadService>();
    builder.Services.AddSingleton<TrafficService>();
    builder.Services.AddSingleton<ConfigService>();

    // D5: IVehicleRegistry — singleton via DI
    builder.Services.AddSingleton<IVehicleRegistry, VehicleRegistry>();
    builder.Services.AddSingleton<IAirCarRegistry, AirCarRegistry>();

    // VehicleClassifier — scoped so it gets a fresh instance per request
    builder.Services.AddScoped<VehicleClassifier>();

    // D6 / E5: Heartbeat monitor — auto-marks stale devices offline
    builder.Services.AddHostedService<HeartbeatMonitorService>();

    // ── In-App Trace Service (singleton ring buffer for floating panel) ──
    builder.Services.AddSingleton<InAppTraceService>();

    var app = builder.Build();

    // Wire the InAppTraceService into the static TraceLogger so every
    // trace line is also pushed to the in-app ring buffer for the floating panel.
    // Done after Build() to avoid creating a duplicate service provider.
    var _traceSvc = app.Services.GetRequiredService<InAppTraceService>();
    var tracePath = Path.Combine(AppContext.BaseDirectory, "inapptrace.json");
    if (File.Exists(tracePath))
    {
        try
        {
            var tj = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(
                File.ReadAllText(tracePath));
            if (tj != null)
            {
                if (tj.TryGetValue("enabled", out var eEl) && eEl.ValueKind == System.Text.Json.JsonValueKind.True)
                    _traceSvc.Enabled = true;
                if (tj.TryGetValue("level", out var lEl) && lEl.ValueKind == System.Text.Json.JsonValueKind.String)
                    _traceSvc.Level = lEl.GetString() ?? "info";
            }
        }
        catch { }
    }
    TraceLogger.OnInAppTrace = (level, module, method, message) => _traceSvc.AddLine(level, module, method, message);

    // ── C5 / FIX: MigrateAsync for BOTH SQLite and PostgreSQL ────────────
    // ROOT CAUSE FIX (SqliteException: no such column FailedLoginAttempts):
    //   EnsureCreated() was used for SQLite — it snapshots the model at DB
    //   creation time and never applies subsequent migrations. Switching to
    //   MigrateAsync() for all providers ensures every migration (including
    //   AddAccountLockout which adds FailedLoginAttempts / LockedUntil) is
    //   applied on startup, regardless of environment.
    //   Idempotent SQLite column guards are added as a safety net for any
    //   pre-existing DB that was created with EnsureCreated().
    using (var scope = app.Services.CreateScope())
    {
        var db        = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger    = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        var isPostgres = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DATABASE_URL"));

        // ── Step 1: Run all pending EF migrations ─────────────────────────
        try
        {
            await db.Database.MigrateAsync();   // works for both SQLite and PostgreSQL
            logger.LogInformation("Startup: EF migrations applied (provider={Provider})",
                isPostgres ? "PostgreSQL" : "SQLite");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Startup: MigrateAsync failed — running column safety guards.");
        }

        // ── Step 1b: Ensure ALL critical tables exist ──────────────────────
        // Brute-force safety net: if MigrateAsync() failed or the database was
        // corrupted/stale (e.g. git checkout overwrote mergesafe.db), create
        // every table with CREATE TABLE IF NOT EXISTS so the app can boot.
        // This is idempotent — existing tables are not affected.
        if (!isPostgres)
        {
            var createTables = new[]
            {
                // Highways
                @"CREATE TABLE IF NOT EXISTS Highways (
                    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name        TEXT NOT NULL,
                    HighwayId   TEXT NOT NULL,
                    State       TEXT,
                    Description TEXT,
                    IsActive    INTEGER NOT NULL DEFAULT 1,
                    CreatedDate TEXT NOT NULL DEFAULT (datetime('now'))
                )",
                // MergeZones
                @"CREATE TABLE IF NOT EXISTS MergeZones (
                    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                    ZoneName        TEXT NOT NULL,
                    ZoneId          TEXT NOT NULL,
                    HighwayId       TEXT NOT NULL,
                    MileMarker      REAL,
                    Latitude        REAL,
                    Longitude       REAL,
                    GeofenceRadius  INTEGER NOT NULL DEFAULT 500,
                    Status          TEXT NOT NULL DEFAULT 'active',
                    AltitudeMeters  REAL DEFAULT 0,
                    CreatedDate     TEXT NOT NULL DEFAULT (datetime('now'))
                )",
                // SwitchServers
                @"CREATE TABLE IF NOT EXISTS SwitchServers (
                    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                    ServerName      TEXT NOT NULL,
                    ServerId        TEXT NOT NULL,
                    ZoneId          TEXT,
                    HighwayId       TEXT NOT NULL,
                    IpAddress       TEXT,
                    Port            INTEGER NOT NULL DEFAULT 5000,
                    Status          TEXT NOT NULL DEFAULT 'offline',
                    FirmwareVersion TEXT,
                    UptimeSeconds   INTEGER NOT NULL DEFAULT 0,
                    CpuPercent      REAL NOT NULL DEFAULT 0,
                    MemoryPercent   REAL NOT NULL DEFAULT 0,
                    LastHeartbeat   TEXT NOT NULL DEFAULT (datetime('now')),
                    AltitudeMinMeters  REAL,
                    AltitudeMaxMeters  REAL,
                    AltitudeWidthMeters REAL,
                    GpsLocation     TEXT,
                    CreatedDate     TEXT NOT NULL DEFAULT (datetime('now'))
                )",
                // SensorDevices
                @"CREATE TABLE IF NOT EXISTS SensorDevices (
                    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                    DeviceName      TEXT NOT NULL,
                    DeviceId        TEXT NOT NULL,
                    DeviceType      TEXT NOT NULL,
                    ZoneId          TEXT,
                    HighwayId       TEXT NOT NULL,
                    MileMarker      REAL,
                    Latitude        REAL,
                    Longitude       REAL,
                    Status          TEXT NOT NULL DEFAULT 'offline',
                    FirmwareVersion TEXT,
                    AltitudeMeters  REAL DEFAULT 0,
                    LastHeartbeat   TEXT NOT NULL DEFAULT (datetime('now')),
                    CreatedDate     TEXT NOT NULL DEFAULT (datetime('now'))
                )",
                // TriangulationConfigs
                @"CREATE TABLE IF NOT EXISTS TriangulationConfigs (
                    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                    ZoneId          TEXT NOT NULL,
                    HighwayId       TEXT NOT NULL,
                    GeofenceRadius  INTEGER NOT NULL DEFAULT 500,
                    IsActive        INTEGER NOT NULL DEFAULT 1,
                    Switch1Label    TEXT, Switch1ServerId TEXT,
                    Switch1Lat      REAL, Switch1Lon REAL,
                    Switch2Label    TEXT, Switch2ServerId TEXT,
                    Switch2Lat      REAL, Switch2Lon REAL,
                    Switch3Label    TEXT, Switch3ServerId TEXT,
                    Switch3Lat      REAL, Switch3Lon REAL,
                    CreatedDate     TEXT NOT NULL DEFAULT (datetime('now'))
                )",
                // VehicleEvents
                @"CREATE TABLE IF NOT EXISTS VehicleEvents (
                    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                    EventType       TEXT NOT NULL DEFAULT 'vehicle_pass',
                    ZoneId          TEXT,
                    HighwayId       TEXT NOT NULL,
                    DeviceId        TEXT,
                    VehicleId       TEXT,
                    SpeedMph        REAL,
                    Latitude        REAL,
                    Longitude       REAL,
                    Heading         REAL,
                    Direction       TEXT,
                    Payload         TEXT,
                    AltitudeMeters  REAL DEFAULT 0,
                    VehicleMode     TEXT NOT NULL DEFAULT 'ground',
                    VehicleCategory TEXT NOT NULL DEFAULT 'sedan',
                    VehicleClassJson TEXT,
                    IsAirFlyCar     TEXT NOT NULL DEFAULT 'N',
                    CreatedDate     TEXT NOT NULL DEFAULT (datetime('now'))
                )",
                // InputFormatConfigs
                @"CREATE TABLE IF NOT EXISTS InputFormatConfigs (
                    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                    FormatName      TEXT NOT NULL,
                    SourceId        TEXT,
                    SourceType      TEXT NOT NULL DEFAULT 'tomtom',
                    InputSource     TEXT,
                    Description     TEXT,
                    EnabledFieldsRaw TEXT,
                    CreatedDate     TEXT NOT NULL DEFAULT (datetime('now'))
                )",
                // SamplePayloads
                @"CREATE TABLE IF NOT EXISTS SamplePayloads (
                    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                    ConfigId    INTEGER,
                    SourceType  TEXT NOT NULL DEFAULT 'tomtom',
                    Label       TEXT,
                    Payload     TEXT,
                    IsValid     INTEGER NOT NULL DEFAULT 1,
                    CreatedDate TEXT NOT NULL DEFAULT (datetime('now'))
                )",
                // UserProfiles
                @"CREATE TABLE IF NOT EXISTS UserProfiles (
                    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                    UserId          TEXT,
                    FullName        TEXT NOT NULL,
                    UserType        TEXT NOT NULL DEFAULT 'operator',
                    Phone           TEXT,
                    Address         TEXT,
                    HighwayId       TEXT,
                    HighwayName     TEXT,
                    DeviceIdsRaw    TEXT,
                    Notes           TEXT,
                    Password        TEXT,
                    IsActive        INTEGER NOT NULL DEFAULT 1,
                    FailedLoginAttempts INTEGER NOT NULL DEFAULT 0,
                    LockedUntil     TEXT,
                    CreatedDate     TEXT NOT NULL DEFAULT (datetime('now'))
                )",
                // AuditLogs
                @"CREATE TABLE IF NOT EXISTS AuditLogs (
                    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                    UserId      TEXT NOT NULL DEFAULT '',
                    FullName    TEXT NOT NULL DEFAULT '',
                    HighwayId   TEXT,
                    Controller  TEXT NOT NULL DEFAULT '',
                    Action      TEXT NOT NULL DEFAULT '',
                    EntityType  TEXT,
                    EntityId    TEXT,
                    Summary     TEXT,
                    IpAddress   TEXT,
                    CreatedDate TEXT NOT NULL DEFAULT (datetime('now'))
                )",
                // SimulationStatus — single-row table tracking sim state across restarts
                @"CREATE TABLE IF NOT EXISTS SimulationStatuses (
                    Id            INTEGER PRIMARY KEY AUTOINCREMENT,
                    IsRunning     INTEGER NOT NULL DEFAULT 0,
                    HighwayId     TEXT,
                    ZoneId        TEXT,
                    ServerId      TEXT,
                    SourceType    TEXT,
                    TotalPosted   INTEGER NOT NULL DEFAULT 0,
                    LastHeartbeat TEXT NOT NULL DEFAULT (datetime('now')),
                    StoppedAt     TEXT
                )",
            };

            foreach (var sql in createTables)
            {
                try { await db.Database.ExecuteSqlRawAsync(sql); }
                catch (Exception exTable)
                {
                    logger.LogDebug("Startup: CREATE TABLE skipped: {Message}", exTable.Message);
                }
            }

            // Create critical indexes (idempotent)
            var createIndexes = new[]
            {
                "CREATE INDEX IF NOT EXISTS IX_MergeZones_HighwayId     ON MergeZones (HighwayId)",
                "CREATE INDEX IF NOT EXISTS IX_SwitchServers_HighwayId  ON SwitchServers (HighwayId)",
                "CREATE INDEX IF NOT EXISTS IX_SensorDevices_HighwayId  ON SensorDevices (HighwayId)",
                "CREATE INDEX IF NOT EXISTS IX_VehicleEvents_HighwayId  ON VehicleEvents (HighwayId)",
                "CREATE INDEX IF NOT EXISTS IX_UserProfiles_HighwayId   ON UserProfiles (HighwayId)",
                "CREATE INDEX IF NOT EXISTS IX_SwitchServers_HighwayId_ZoneId ON SwitchServers (HighwayId, ZoneId)",
                "CREATE INDEX IF NOT EXISTS IX_SensorDevices_HighwayId_ZoneId ON SensorDevices (HighwayId, ZoneId)",
                "CREATE INDEX IF NOT EXISTS IX_VehicleEvents_HighwayId_ZoneId ON VehicleEvents (HighwayId, ZoneId)",
                "CREATE INDEX IF NOT EXISTS IX_VehicleEvents_VehicleMode     ON VehicleEvents (VehicleMode)",
                "CREATE INDEX IF NOT EXISTS IX_VehicleEvents_VehicleCategory ON VehicleEvents (VehicleCategory)",
                "CREATE INDEX IF NOT EXISTS IX_AuditLogs_UserId                ON AuditLogs (UserId)",
                "CREATE INDEX IF NOT EXISTS IX_AuditLogs_CreatedDate           ON AuditLogs (CreatedDate)",
                "CREATE INDEX IF NOT EXISTS IX_AuditLogs_HighwayId_CreatedDate ON AuditLogs (HighwayId, CreatedDate)",
            };
            foreach (var sql in createIndexes)
            {
                try { await db.Database.ExecuteSqlRawAsync(sql); }
                catch { /* index already exists — harmless */ }
            }

            logger.LogInformation("Startup: All {Count} tables + {IdxCount} indexes verified via CREATE IF NOT EXISTS.",
                createTables.Length, createIndexes.Length);
        }

        // ── Step 2: Idempotent column guards ──────────────────────────────
        // ── Step 2: Idempotent column guards ──────────────────────────────
        // Safety net for pre-existing DBs created before any migration was added.
        // SQLite does NOT support "ADD COLUMN IF NOT EXISTS" — each statement is
        // wrapped in try/catch; "duplicate column" errors are silently swallowed.
        // PostgreSQL supports "ADD COLUMN IF NOT EXISTS" natively.
        // ─── COMPLETE COLUMN INVENTORY (all migrations, all providers) ────────
        //   20260520000000 Initial                    — base schema
        //   20260522000000 AddAccountLockout          — FailedLoginAttempts, LockedUntil
        //   20260619000000 AddAirFlyCarSourceType      — InputFormatConfigs seed only (no columns)
        //   20260620000000 AddAltitudeFields           — AltitudeMeters ×3, AltMin/Max/Width
        //   20260620000001 AddAuditLog                 — AuditLogs table
        //   20260620000002 AddVehicleClassification    — VehicleMode, VehicleCategory, VehicleClassJson
        //   20260620000003 AddSwitchServerGpsLocation  — GpsLocation on SwitchServers
        //   20260621000000 AddIsAirFlyCar              — IsAirFlyCar on VehicleEvents  ← THIS FIX

        var sqliteGuards = isPostgres ? Array.Empty<string>() : new[]
        {
            // 20260522000000 AddAccountLockout
            "ALTER TABLE UserProfiles ADD COLUMN FailedLoginAttempts INTEGER NOT NULL DEFAULT 0",
            "ALTER TABLE UserProfiles ADD COLUMN LockedUntil TEXT",

            // 20260620000000 AddAltitudeFields
            "ALTER TABLE SwitchServers ADD COLUMN AltitudeMinMeters REAL",
            "ALTER TABLE SwitchServers ADD COLUMN AltitudeMaxMeters REAL",
            "ALTER TABLE SwitchServers ADD COLUMN AltitudeWidthMeters REAL",
            "ALTER TABLE VehicleEvents ADD COLUMN AltitudeMeters REAL DEFAULT 0",
            "ALTER TABLE SensorDevices ADD COLUMN AltitudeMeters REAL DEFAULT 0",
            "ALTER TABLE MergeZones    ADD COLUMN AltitudeMeters REAL DEFAULT 0",

            // 20260620000002 AddVehicleClassification
            "ALTER TABLE VehicleEvents ADD COLUMN VehicleMode     TEXT NOT NULL DEFAULT 'ground'",
            "ALTER TABLE VehicleEvents ADD COLUMN VehicleCategory  TEXT NOT NULL DEFAULT 'sedan'",
            "ALTER TABLE VehicleEvents ADD COLUMN VehicleClassJson TEXT",
            "CREATE INDEX IF NOT EXISTS IX_VehicleEvents_VehicleMode     ON VehicleEvents (VehicleMode)",
            "CREATE INDEX IF NOT EXISTS IX_VehicleEvents_VehicleCategory  ON VehicleEvents (VehicleCategory)",

            // 20260620000003 AddSwitchServerGpsLocation
            "ALTER TABLE SwitchServers ADD COLUMN GpsLocation TEXT",

            // 20260621000000 AddIsAirFlyCar  ← THE FIX for the current exception
            "ALTER TABLE VehicleEvents ADD COLUMN IsAirFlyCar TEXT NOT NULL DEFAULT 'N'",
        };

        var pgGuards = !isPostgres ? Array.Empty<string>() : new[]
        {
            // 20260522000000 AddAccountLockout
            @"ALTER TABLE ""UserProfiles"" ADD COLUMN IF NOT EXISTS ""FailedLoginAttempts"" INTEGER NOT NULL DEFAULT 0",
            @"ALTER TABLE ""UserProfiles"" ADD COLUMN IF NOT EXISTS ""LockedUntil"" TIMESTAMPTZ",

            // 20260620000000 AddAltitudeFields
            @"ALTER TABLE ""SwitchServers"" ADD COLUMN IF NOT EXISTS ""AltitudeMinMeters"" DOUBLE PRECISION",
            @"ALTER TABLE ""SwitchServers"" ADD COLUMN IF NOT EXISTS ""AltitudeMaxMeters"" DOUBLE PRECISION",
            @"ALTER TABLE ""SwitchServers"" ADD COLUMN IF NOT EXISTS ""AltitudeWidthMeters"" DOUBLE PRECISION",
            @"ALTER TABLE ""VehicleEvents"" ADD COLUMN IF NOT EXISTS ""AltitudeMeters"" DOUBLE PRECISION DEFAULT 0",
            @"ALTER TABLE ""SensorDevices"" ADD COLUMN IF NOT EXISTS ""AltitudeMeters"" DOUBLE PRECISION DEFAULT 0",
            @"ALTER TABLE ""MergeZones""    ADD COLUMN IF NOT EXISTS ""AltitudeMeters"" DOUBLE PRECISION DEFAULT 0",

            // 20260620000001 AddAuditLog
            @"CREATE TABLE IF NOT EXISTS ""AuditLogs"" (
                ""Id""          BIGSERIAL PRIMARY KEY,
                ""UserId""      VARCHAR(50)  NOT NULL DEFAULT '',
                ""FullName""    VARCHAR(100) NOT NULL DEFAULT '',
                ""HighwayId""   VARCHAR(50),
                ""Controller""  VARCHAR(60)  NOT NULL DEFAULT '',
                ""Action""      VARCHAR(30)  NOT NULL DEFAULT '',
                ""EntityType""  VARCHAR(60),
                ""EntityId""    VARCHAR(80),
                ""Summary""     VARCHAR(500),
                ""IpAddress""   VARCHAR(45),
                ""CreatedDate"" TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
            )",
            @"CREATE INDEX IF NOT EXISTS ""IX_AuditLogs_UserId""                ON ""AuditLogs"" (""UserId"")",
            @"CREATE INDEX IF NOT EXISTS ""IX_AuditLogs_CreatedDate""           ON ""AuditLogs"" (""CreatedDate"")",
            @"CREATE INDEX IF NOT EXISTS ""IX_AuditLogs_HighwayId_CreatedDate"" ON ""AuditLogs"" (""HighwayId"", ""CreatedDate"")",

            // 20260620000002 AddVehicleClassification
            @"ALTER TABLE ""VehicleEvents"" ADD COLUMN IF NOT EXISTS ""VehicleMode""      VARCHAR(10)  NOT NULL DEFAULT 'ground'",
            @"ALTER TABLE ""VehicleEvents"" ADD COLUMN IF NOT EXISTS ""VehicleCategory""  VARCHAR(20)  NOT NULL DEFAULT 'sedan'",
            @"ALTER TABLE ""VehicleEvents"" ADD COLUMN IF NOT EXISTS ""VehicleClassJson"" VARCHAR(800)",
            @"CREATE INDEX IF NOT EXISTS ""IX_VehicleEvents_VehicleMode""      ON ""VehicleEvents"" (""VehicleMode"")",
            @"CREATE INDEX IF NOT EXISTS ""IX_VehicleEvents_VehicleCategory""  ON ""VehicleEvents"" (""VehicleCategory"")",

            // 20260620000003 AddSwitchServerGpsLocation
            @"ALTER TABLE ""SwitchServers"" ADD COLUMN IF NOT EXISTS ""GpsLocation"" VARCHAR(60)",

            // 20260621000000 AddIsAirFlyCar  ← THE FIX for the current exception
            @"ALTER TABLE ""VehicleEvents"" ADD COLUMN IF NOT EXISTS ""IsAirFlyCar"" VARCHAR(1) NOT NULL DEFAULT 'N'",

            // Mark ALL migrations as applied so MigrateAsync never double-runs them
            @"INSERT INTO ""__EFMigrationsHistory"" (""MigrationId"", ""ProductVersion"") VALUES ('20260522000000_AddAccountLockout',          '8.0.0') ON CONFLICT DO NOTHING",
            @"INSERT INTO ""__EFMigrationsHistory"" (""MigrationId"", ""ProductVersion"") VALUES ('20260619000000_AddAirFlyCarSourceType',      '8.0.0') ON CONFLICT DO NOTHING",
            @"INSERT INTO ""__EFMigrationsHistory"" (""MigrationId"", ""ProductVersion"") VALUES ('20260620000000_AddAltitudeFields',           '8.0.0') ON CONFLICT DO NOTHING",
            @"INSERT INTO ""__EFMigrationsHistory"" (""MigrationId"", ""ProductVersion"") VALUES ('20260620000001_AddAuditLog',                 '8.0.0') ON CONFLICT DO NOTHING",
            @"INSERT INTO ""__EFMigrationsHistory"" (""MigrationId"", ""ProductVersion"") VALUES ('20260620000002_AddVehicleClassification',    '8.0.0') ON CONFLICT DO NOTHING",
            @"INSERT INTO ""__EFMigrationsHistory"" (""MigrationId"", ""ProductVersion"") VALUES ('20260620000003_AddSwitchServerGpsLocation',  '8.0.0') ON CONFLICT DO NOTHING",
            @"INSERT INTO ""__EFMigrationsHistory"" (""MigrationId"", ""ProductVersion"") VALUES ('20260621000000_AddIsAirFlyCar',              '8.0.0') ON CONFLICT DO NOTHING",
        };

        var activeGuards = isPostgres ? pgGuards : sqliteGuards;
        foreach (var sql in activeGuards)
        {
            try
            {
                await db.Database.ExecuteSqlRawAsync(sql);
            }
            catch
            {
                // Column/index/table already exists — safe to ignore.
                // SQLite does not support ADD COLUMN IF NOT EXISTS, so
                // duplicate-column errors on re-start are expected and harmless.
                logger.LogDebug("Startup guard skipped (already applied): {Preview}",
                    sql.Split('\n')[0].Trim()[..Math.Min(70, sql.Split('\n')[0].Trim().Length)]);
            }
        }

        // ── Step 2b: Clean up stale simulation state ──────────────────────
        // On startup, check the SimulationStatuses table. If a simulation was
        // marked as running but the last heartbeat is older than 2 minutes,
        // the previous session died without calling SimulationStop.  Mark it
        // as stopped and clean up orphaned simulation telemetry.
        if (!isPostgres)
        {
            try
            {
                var simStatus = await db.SimulationStatuses
                    .OrderByDescending(s => s.Id)
                    .FirstOrDefaultAsync();

                if (simStatus != null && simStatus.IsRunning)
                {
                    var heartbeatAge = DateTime.UtcNow - simStatus.LastHeartbeat;
                    if (heartbeatAge.TotalMinutes > 2)
                    {
                        logger.LogWarning("Startup: Stale simulation detected — last heartbeat {Age:0.0} min ago. "
                            + "Cleaning up simulation telemetry.", heartbeatAge.TotalMinutes);

                        // Mark as stopped
                        simStatus.IsRunning = false;
                        simStatus.StoppedAt  = DateTime.UtcNow;
                        db.SimulationStatuses.Update(simStatus);

                        // Clean up orphaned simulation data
                        var simPayloads = db.SamplePayloads
                            .Where(p => p.Label != null && p.Label.StartsWith("Simulation ["));
                        db.SamplePayloads.RemoveRange(simPayloads);

                        var simEvents = db.VehicleEvents
                            .Where(v => v.VehicleId != null && v.VehicleId.StartsWith("SIM-"));
                        db.VehicleEvents.RemoveRange(simEvents);

                        await db.SaveChangesAsync();
                        logger.LogInformation("Startup: Stale simulation cleaned up — DB is ready.");
                    }
                    else
                    {
                        logger.LogInformation("Startup: Simulation was recently active (heartbeat {Age:0.0} min ago) "
                            + "— leaving as-is for client to resume.", heartbeatAge.TotalMinutes);
                    }
                }
            }
            catch (Exception exSim)
            {
                logger.LogError(exSim, "Startup: Simulation status check failed — continuing.");
            }
        }

        // ── Step 3: Seed reference data ───────────────────────────────────
        // Force-seed if Highways is empty — covers cases where the local DB file
        // was created by a previous run before migrations were complete, or if
        // mergesafe.db was deleted and EF just recreated it via MigrateAsync().
        try
        {
            await DbInitializer.SeedAsync(db, logger);
            logger.LogInformation("Startup: Database ready — migrations applied, seed data verified.");
        }
        catch (Exception ex) { logger.LogError(ex, "Startup: Seed failed — {Message}", ex.Message); }
    }
    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Home/Error");
        app.UseHsts();
    }

    app.UseResponseCompression();

    // Security headers
    app.Use(async (ctx, next) =>
    {
        ctx.Response.Headers["X-Frame-Options"]         = "DENY";
        ctx.Response.Headers["X-Content-Type-Options"]  = "nosniff";
        ctx.Response.Headers["Referrer-Policy"]         = "strict-origin-when-cross-origin";
        ctx.Response.Headers["Content-Security-Policy"] =
            "default-src 'self'; " +
            "script-src 'self' 'unsafe-inline' cdn.jsdelivr.net unpkg.com cdnjs.cloudflare.com; " +
            "style-src 'self' 'unsafe-inline' cdn.jsdelivr.net unpkg.com; " +
            "font-src 'self' cdn.jsdelivr.net; " +
            "img-src 'self' data: *.openstreetmap.org cdn.jsdelivr.net; " +
            "connect-src 'self' cdn.jsdelivr.net; " +
            "frame-ancestors 'none'";
        ctx.Response.Headers.Remove("X-Powered-By");
        await next();
    });

    // E6: Serilog request logging — structured HTTP access log
    app.UseSerilogRequestLogging(opts =>
    {
        opts.MessageTemplate =
            "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.000}ms";
        opts.GetLevel = (ctx, elapsed, ex) =>
            ex != null || ctx.Response.StatusCode >= 500
                ? Serilog.Events.LogEventLevel.Error
                : ctx.Response.StatusCode >= 400
                    ? Serilog.Events.LogEventLevel.Warning
                    : Serilog.Events.LogEventLevel.Information;
    });

    app.UseStaticFiles();
    app.UseRouting();

    // Session MUST be before any middleware or controller that reads HttpContext.Session.
    // Previously placed after MapControllerRoute — this prevented Portal/Index from
    // reading the session, causing an exception before the Highways query result
    // reached the view (empty dropdown on login page).
    app.UseSession();

    // E3: Rate limiter middleware — must be after UseRouting
    app.UseRateLimiter();

    app.UseOutputCache();
    app.UseAuthorization();

    // E3: Apply rate-limit policies to specific routes
    app.MapControllerRoute(
        name: "portal_login",
        pattern: "Portal/Login",
        defaults: new { controller = "Portal", action = "Login" })
       .RequireRateLimiting(RateLimiterExtensions.LoginPolicy);

    app.MapControllerRoute(
        name: "api_ingest",
        pattern: "api/events/ingest",
        defaults: new { controller = "Api", action = "IngestEvent" })
       .RequireRateLimiting(RateLimiterExtensions.IngestPolicy);

    app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Portal}/{action=Index}/{id?}");

    var port = Environment.GetEnvironmentVariable("PORT") ?? "5000";
    app.Urls.Add($"http://0.0.0.0:{port}");

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

static string ParsePostgresUrl(string url)
{
    try
    {
        var m = System.Text.RegularExpressions.Regex.Match(url,
            @"^(?:postgresql|postgres)://([^:@]+)(?::([^@]*))?@([^/:?]+)(?::(\d+))?/([^?]*)(?:\?(.*))?$");
        if (!m.Success) return url;
        var user = m.Groups[1].Value; var pass = m.Groups[2].Value;
        var host = m.Groups[3].Value; var port = m.Groups[4].Success ? m.Groups[4].Value : "5432";
        var db   = m.Groups[5].Value; var qs   = m.Groups[6].Value;
        var conn = $"Host={host};Port={port};Database={db};Username={user};Password={pass};";
        if (!string.IsNullOrEmpty(qs))
            foreach (var param in qs.Split('&'))
            {
                var kv = param.Split('=', 2);
                if (kv.Length == 2 && kv[0].Equals("sslmode", StringComparison.OrdinalIgnoreCase))
                    conn += $"SSL Mode={kv[1]};";
            }
        return conn;
    }
    catch { return url; }
}




