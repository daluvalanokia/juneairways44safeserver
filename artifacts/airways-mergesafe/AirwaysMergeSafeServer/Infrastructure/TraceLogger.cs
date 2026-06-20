using System.Text;

namespace AirwaysMergeSafeServer.Infrastructure;

/// <summary>
/// Application-wide function entry/exit trace logger.
/// Writes to /tmp/trace_{timestamp}.log  (Linux equivalent of C:\temp\trace_{timestamp}.log).
/// Thread-safe via lock on the StreamWriter.  Initialised once at startup via Initialise().
/// Non-fatal: if the file cannot be created, tracing silently disables itself.
/// </summary>
public static class TraceLogger
{
    private static readonly object _lock    = new();
    private static StreamWriter?   _writer;
    private static bool            _enabled = false;
    private static string          _logPath = string.Empty;

    /// <summary>
    /// Call once in Program.cs before the host is built.
    /// Creates /tmp/trace_yyyyMMdd_HHmmss.log and writes an INIT entry.
    /// </summary>
    public static void Initialise()
    {
        try
        {
            var stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            const string dir = "/tmp";
            Directory.CreateDirectory(dir);
            _logPath = Path.Combine(dir, $"trace_{stamp}.log");
            _writer  = new StreamWriter(_logPath, append: false, Encoding.UTF8) { AutoFlush = true };
            _enabled = true;
            Write("INIT ", "TraceLogger", "Initialise",
                $"Trace log started — {_logPath}");
        }
        catch
        {
            _enabled = false; // non-fatal: tracing silently disabled
        }
    }

    /// <summary>Log function entry. Call as the very first statement in each method.</summary>
    public static void Enter(string module, string method, string? args = null)
    {
        if (!_enabled) return;
        Write("ENTER", module, method, args != null ? $"ENTER {method}({args})" : $"ENTER {method}()");
    }

    /// <summary>Log normal function exit. Call just before returning.</summary>
    public static void Exit(string module, string method, string? result = null)
    {
        if (!_enabled) return;
        Write("EXIT ", module, method, result != null ? $"EXIT  {method} → {result}" : $"EXIT  {method}");
    }

    /// <summary>Log a caught exception inside a try/catch block.</summary>
    public static void Error(string module, string method, Exception ex)
    {
        if (!_enabled) return;
        Write("ERROR", module, method,
            $"ERROR {method} — {ex.GetType().Name}: {ex.Message}");
    }

    /// <summary>Log an informational message (lifecycle events, warnings, etc.).</summary>
    public static void Info(string module, string method, string message)
    {
        if (!_enabled) return;
        Write("INFO ", module, method, message);
    }

    /// <summary>Absolute path of the current trace log file (empty if disabled).</summary>
    public static string LogPath => _logPath;

    // ── Internal writer ────────────────────────────────────────────────
    private static void Write(string level, string module, string method, string message)
    {
        try
        {
            var line = $"[{DateTime.UtcNow:HH:mm:ss.fff}] [{level}] [{module}.{method}] {message}";
            lock (_lock) { _writer?.WriteLine(line); }
        }
        catch { /* never throw from the trace logger */ }
    }
}
