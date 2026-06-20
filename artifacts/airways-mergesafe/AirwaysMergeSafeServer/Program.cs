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

    // VehicleClassifier — scoped so it gets a fresh instance per request
    builder.Services.AddScoped<VehicleClassifier>();

    // D6 / E5: Heartbeat monitor — auto-marks stale devices offline
    builder.Services.AddHostedService<HeartbeatMonitorService>();

    var app = builder.Build();

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

            // 20260620000001 AddAuditLog
            @"CREATE TABLE IF NOT EXISTS AuditLogs (
                Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                UserId      TEXT    NOT NULL DEFAULT '',
                FullName    TEXT    NOT NULL DEFAULT '',
                HighwayId   TEXT,
                Controller  TEXT    NOT NULL DEFAULT '',
                Action      TEXT    NOT NULL DEFAULT '',
                EntityType  TEXT,
                EntityId    TEXT,
                Summary     TEXT,
                IpAddress   TEXT,
                CreatedDate TEXT    NOT NULL DEFAULT (datetime('now'))
            )",
            "CREATE INDEX IF NOT EXISTS IX_AuditLogs_UserId                ON AuditLogs (UserId)",
            "CREATE INDEX IF NOT EXISTS IX_AuditLogs_CreatedDate           ON AuditLogs (CreatedDate)",
            "CREATE INDEX IF NOT EXISTS IX_AuditLogs_HighwayId_CreatedDate ON AuditLogs (HighwayId, CreatedDate)",

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

        // ── Step 3: Seed reference data ───────────────────────────────────
        try
        {
            DbInitializer.Seed(db);
            logger.LogInformation("Startup: Database ready — migrations applied, seed data loaded.");
        }
        catch (Exception ex) { logger.LogError(ex, "Startup: Seed failed."); }
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
            "img-src 'self' data: *.tile.openstreetmap.org; " +
            "connect-src 'self'; " +
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

    // E3: Rate limiter middleware — must be after UseRouting
    app.UseRateLimiter();

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

    app.UseSession();
    app.UseOutputCache();
    app.UseAuthorization();

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




