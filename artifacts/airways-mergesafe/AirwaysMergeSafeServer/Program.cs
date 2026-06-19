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

try
{
    var builder = WebApplication.CreateBuilder(args);

    // E6: Full Serilog logging with file + optional Seq sinks
    builder.AddSerilogLogging();

    builder.WebHost.ConfigureKestrel(opts => { opts.AddServerHeader = false; });

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
    });

    // ── Session (secure) ──────────────────────────────────────────────────
    builder.Services.AddSession(options =>
    {
        options.IdleTimeout         = TimeSpan.FromHours(2);
        options.Cookie.HttpOnly     = true;
        options.Cookie.IsEssential  = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite     = SameSiteMode.Strict;
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

    // D6 / E5: Heartbeat monitor — auto-marks stale devices offline
    builder.Services.AddHostedService<HeartbeatMonitorService>();

    var app = builder.Build();

    // ── C5: MigrateAsync at startup (replaces all startup DDL hacks) ──────
    using (var scope = app.Services.CreateScope())
    {
        var db     = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        var isPostgres = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DATABASE_URL"));
        try
        {
            if (isPostgres)
                await db.Database.MigrateAsync();
            else
                db.Database.EnsureCreated();
        }
        catch (Exception ex) { logger.LogError(ex, "Startup: Migration failed — applying safety guards."); }

        if (isPostgres)
        {
            // Safety guards — idempotent ALTER TABLE IF NOT EXISTS for all new columns.
            // Required because the 44-repo migrations contain SQLite-specific syntax in
            // AddAuditLog (datetime('now') default) which can abort the migration chain
            // on PostgreSQL before all columns are applied.
            var guards = new[]
            {
                // Altitude fields (20260620000000_AddAltitudeFields)
                "ALTER TABLE \"SwitchServers\" ADD COLUMN IF NOT EXISTS \"AltitudeMinMeters\" DOUBLE PRECISION",
                "ALTER TABLE \"SwitchServers\" ADD COLUMN IF NOT EXISTS \"AltitudeMaxMeters\" DOUBLE PRECISION",
                "ALTER TABLE \"SwitchServers\" ADD COLUMN IF NOT EXISTS \"AltitudeWidthMeters\" DOUBLE PRECISION",
                "ALTER TABLE \"VehicleEvents\" ADD COLUMN IF NOT EXISTS \"AltitudeMeters\" DOUBLE PRECISION DEFAULT 0",
                "ALTER TABLE \"SensorDevices\" ADD COLUMN IF NOT EXISTS \"AltitudeMeters\" DOUBLE PRECISION DEFAULT 0",
                "ALTER TABLE \"MergeZones\"   ADD COLUMN IF NOT EXISTS \"AltitudeMeters\" DOUBLE PRECISION DEFAULT 0",
                // Vehicle classification fields (20260620000002_AddVehicleClassification)
                "ALTER TABLE \"VehicleEvents\" ADD COLUMN IF NOT EXISTS \"VehicleMode\"     VARCHAR(10)  NOT NULL DEFAULT 'ground'",
                "ALTER TABLE \"VehicleEvents\" ADD COLUMN IF NOT EXISTS \"VehicleCategory\"  VARCHAR(20)  NOT NULL DEFAULT 'sedan'",
                "ALTER TABLE \"VehicleEvents\" ADD COLUMN IF NOT EXISTS \"VehicleClassJson\" VARCHAR(800)",
                // AuditLogs table (20260620000001_AddAuditLog)
                @"CREATE TABLE IF NOT EXISTS ""AuditLogs"" (
                    ""Id""         BIGSERIAL PRIMARY KEY,
                    ""UserId""     VARCHAR(50)  NOT NULL DEFAULT '',
                    ""FullName""   VARCHAR(100) NOT NULL DEFAULT '',
                    ""HighwayId""  VARCHAR(50),
                    ""Controller"" VARCHAR(60)  NOT NULL DEFAULT '',
                    ""Action""     VARCHAR(30)  NOT NULL DEFAULT '',
                    ""EntityType"" VARCHAR(60),
                    ""EntityId""   VARCHAR(80),
                    ""Summary""    VARCHAR(500),
                    ""IpAddress""  VARCHAR(45),
                    ""CreatedDate"" TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
                )",
                "CREATE INDEX IF NOT EXISTS \"IX_AuditLogs_UserId\"              ON \"AuditLogs\" (\"UserId\")",
                "CREATE INDEX IF NOT EXISTS \"IX_AuditLogs_CreatedDate\"         ON \"AuditLogs\" (\"CreatedDate\")",
                "CREATE INDEX IF NOT EXISTS \"IX_AuditLogs_HighwayId_CreatedDate\" ON \"AuditLogs\" (\"HighwayId\", \"CreatedDate\")",
                // VehicleEvents classification indexes
                "CREATE INDEX IF NOT EXISTS \"IX_VehicleEvents_VehicleMode\"     ON \"VehicleEvents\" (\"VehicleMode\")",
                "CREATE INDEX IF NOT EXISTS \"IX_VehicleEvents_VehicleCategory\"  ON \"VehicleEvents\" (\"VehicleCategory\")",
                // Mark new migrations as applied so MigrateAsync skips them next run
                "INSERT INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES ('20260619000000_AddAirFlyCarSourceType', '8.0.0') ON CONFLICT DO NOTHING",
                "INSERT INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES ('20260620000000_AddAltitudeFields', '8.0.0') ON CONFLICT DO NOTHING",
                "INSERT INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES ('20260620000001_AddAuditLog', '8.0.0') ON CONFLICT DO NOTHING",
                "INSERT INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES ('20260620000002_AddVehicleClassification', '8.0.0') ON CONFLICT DO NOTHING",
            };
            foreach (var sql in guards)
            {
                try { db.Database.ExecuteSqlRaw(sql); }
                catch (Exception ex) { logger.LogWarning(ex, "Startup guard skipped: {Sql}", sql[..Math.Min(60,sql.Length)]); }
            }
        }

        try
        {
            DbInitializer.Seed(db);
            logger.LogInformation("Startup: Database ready.");
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
