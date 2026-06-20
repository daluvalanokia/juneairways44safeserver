using System.Text.Json;
using AirwaysMergeSafeServer.Infrastructure;

namespace AirwaysMergeSafeServer.Services;

/// <summary>
/// A4 FIX: TomTom key resolved from environment variable first, then config/file.
///         SaveTomTomKey writes to file only when not in a read-only environment
///         (container deployments should use TOMTOM_API_KEY env var instead).
///         ClearTomTomKey clears file + notifies callers to unset env var.
/// D7 NOTE: AllowedHosts should be locked to the actual domain in production
///          appsettings — documented here as a runtime reminder.
/// </summary>
public class ConfigService
{
    private readonly IConfigurationRoot? _cfgRoot;
    private readonly string              _keyFilePath;

    public ConfigService(IConfiguration cfg)
    {
        _cfgRoot     = cfg as IConfigurationRoot;
        _keyFilePath = Path.Combine(AppContext.BaseDirectory, "tomtomkey.json");
    }

    // A4 FIX: env var takes priority — safe in read-only containers
    public string? GetTomTomKey()
    {
        TraceLogger.Enter("ConfigService", nameof(GetTomTomKey));
        try
        {
            var envKey = Environment.GetEnvironmentVariable("TOMTOM_API_KEY");
            if (!string.IsNullOrWhiteSpace(envKey))
            {
                TraceLogger.Exit("ConfigService", nameof(GetTomTomKey), "env-var");
                return envKey;
            }
            var result = _cfgRoot?["TomTomApiKey"];
            TraceLogger.Exit("ConfigService", nameof(GetTomTomKey), result != null ? "config" : "null");
            return result;
        }
        catch (Exception ex) { TraceLogger.Error("ConfigService", nameof(GetTomTomKey), ex); throw; }
    }

    public void SaveTomTomKey(string apiKey)
    {
        TraceLogger.Enter("ConfigService", nameof(SaveTomTomKey));
        // A4 FIX: attempt file write; if running read-only, log and skip gracefully
        try
        {
            var doc = new Dictionary<string, string> { { "TomTomApiKey", apiKey } };
            File.WriteAllText(_keyFilePath,
                JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true }));
            _cfgRoot?.Reload();
            TraceLogger.Exit("ConfigService", nameof(SaveTomTomKey), "saved");
        }
        catch (UnauthorizedAccessException ex)
        {
            TraceLogger.Error("ConfigService", nameof(SaveTomTomKey), ex);
            // In read-only container — caller should set TOMTOM_API_KEY env var
        }
        catch (IOException ex)
        {
            TraceLogger.Error("ConfigService", nameof(SaveTomTomKey), ex);
            // Non-fatal; env var is the authoritative source in production
        }
    }

    public void ClearTomTomKey()
    {
        TraceLogger.Enter("ConfigService", nameof(ClearTomTomKey));
        try
        {
            if (File.Exists(_keyFilePath)) File.Delete(_keyFilePath);
            _cfgRoot?.Reload();
            TraceLogger.Exit("ConfigService", nameof(ClearTomTomKey));
        }
        catch (Exception ex) { TraceLogger.Error("ConfigService", nameof(ClearTomTomKey), ex); /* non-fatal in read-only environments */ }
    }

    public bool IsReadOnlyEnvironment()
    {
        TraceLogger.Enter("ConfigService", nameof(IsReadOnlyEnvironment));
        try
        {
            var probe = Path.Combine(AppContext.BaseDirectory, ".writable_probe");
            File.WriteAllText(probe, "test");
            File.Delete(probe);
            TraceLogger.Exit("ConfigService", nameof(IsReadOnlyEnvironment), "writable");
            return false;
        }
        catch (Exception ex) { TraceLogger.Error("ConfigService", nameof(IsReadOnlyEnvironment), ex); return true; }
    }
}
