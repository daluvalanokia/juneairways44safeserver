using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace AirwaysMergeSafeServer.Infrastructure;

/// <summary>
/// E6: Serilog structured logging.
///
/// Sinks configured:
///   1. Console   — compact JSON format (production) or coloured text (dev)
///   2. File      — rolling daily log files in /logs/mss-.log, 7-day retention
///   3. Seq       — optional; set SEQ_URL env var to enable (e.g. http://seq:5341)
///
/// Log levels:
///   • Default: Information
///   • Microsoft.AspNetCore: Warning  (reduces HTTP pipeline noise)
///   • Microsoft.EntityFrameworkCore: Warning (suppresses SQL echo in prod)
///   • Security events: always written at Warning or above
///
/// All existing _logger.LogXxx calls automatically route through Serilog
/// — no controller code changes required.
///
/// NuGet packages required (add to .csproj):
///   Serilog.AspNetCore            8.0.0
///   Serilog.Sinks.Console         5.0.0
///   Serilog.Sinks.File            5.0.0
///   Serilog.Sinks.Seq             6.0.0   (optional)
///   Serilog.Formatting.Compact    2.0.0
/// </summary>
public static class LoggingExtensions
{
    public static WebApplicationBuilder AddSerilogLogging(this WebApplicationBuilder builder)
    {
        var isDev  = builder.Environment.IsDevelopment();
        var seqUrl = Environment.GetEnvironmentVariable("SEQ_URL");
        var logDir = Path.Combine(AppContext.BaseDirectory, "logs");
        Directory.CreateDirectory(logDir);

        var logConfig = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft",                       LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.AspNetCore",            LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore",   LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", "AirwaysMergeSafeServer")
            .Enrich.WithProperty("Environment", builder.Environment.EnvironmentName);

        // Console sink — compact JSON in production, pretty in dev
        if (isDev)
            logConfig.WriteTo.Console(outputTemplate:
                "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}");
        else
            logConfig.WriteTo.Console(new CompactJsonFormatter());

        // File sink — rolling daily, 7-day retention
        logConfig.WriteTo.File(
            formatter: new CompactJsonFormatter(),
            path: Path.Combine(logDir, "mss-.log"),
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 7,
            shared: false);

        // Seq sink — optional, enabled by SEQ_URL env var
        if (!string.IsNullOrWhiteSpace(seqUrl))
        {
            logConfig.WriteTo.Seq(seqUrl,
                restrictedToMinimumLevel: LogEventLevel.Information);
        }

        Log.Logger = logConfig.CreateLogger();

        builder.Host.UseSerilog();

        return builder;
    }
}
