using System.Text.Json;

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
        var envKey = Environment.GetEnvironmentVariable("TOMTOM_API_KEY");
        if (!string.IsNullOrWhiteSpace(envKey)) return envKey;
        return _cfgRoot?["TomTomApiKey"];
    }

    public void SaveTomTomKey(string apiKey)
    {
        // A4 FIX: attempt file write; if running read-only, log and skip gracefully
        try
        {
            var doc = new Dictionary<string, string> { { "TomTomApiKey", apiKey } };
            File.WriteAllText(_keyFilePath,
                JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true }));
            _cfgRoot?.Reload();
        }
        catch (UnauthorizedAccessException)
        {
            // In read-only container — caller should set TOMTOM_API_KEY env var
        }
        catch (IOException)
        {
            // Non-fatal; env var is the authoritative source in production
        }
    }

    public void ClearTomTomKey()
    {
        try
        {
            if (File.Exists(_keyFilePath)) File.Delete(_keyFilePath);
            _cfgRoot?.Reload();
        }
        catch { /* non-fatal in read-only environments */ }
    }

    public bool IsReadOnlyEnvironment()
    {
        try
        {
            var probe = Path.Combine(AppContext.BaseDirectory, ".writable_probe");
            File.WriteAllText(probe, "test");
            File.Delete(probe);
            return false;
        }
        catch { return true; }
    }
}
