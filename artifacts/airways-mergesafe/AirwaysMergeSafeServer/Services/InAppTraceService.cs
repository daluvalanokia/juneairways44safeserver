using System.Collections.Concurrent;

namespace AirwaysMergeSafeServer.Services;

/// <summary>
/// Singleton service that holds the last 50 trace lines in a ring buffer.
/// When enabled via Settings, the floating bottom panel polls GetRecent()
/// to display the latest trace output for debugging data-loading issues.
/// </summary>
public class InAppTraceService
{
    private readonly ConcurrentQueue<TraceLine> _buffer = new();
    private const int MaxLines = 50;

    /// <summary>Master toggle — when false, AddLine is a no-op.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Minimum severity to capture: "info", "warning", or "error".</summary>
    public string Level { get; set; } = "info";

    // Level → numeric priority for threshold comparison
    private static int Pri(string lvl) => lvl switch
    {
        "info" or "ENTER" or "EXIT " or "INFO " => 0,
        "warning" or "WARN "                    => 1,
        "error"   or "ERROR"                    => 2,
        _                                         => 0
    };

    /// <summary>Add a line to the ring buffer (if enabled and meets min level).</summary>
    public void AddLine(string level, string message)
    {
        if (!Enabled) return;
        if (Pri(level) < Pri(Level)) return;

        _buffer.Enqueue(new TraceLine
        {
            Timestamp = DateTime.UtcNow.ToString("HH:mm:ss.fff"),
            Level      = level.Trim(),
            Message    = message
        });

        while (_buffer.Count > MaxLines)
            _buffer.TryDequeue(out _);
    }

    /// <summary>Returns the last N trace lines (default 2 for the floating panel).</summary>
    public List<TraceLine> GetRecent(int count = 2)
    {
        var all = _buffer.ToArray();
        return all.Length <= count
            ? all.ToList()
            : all.Skip(all.Length - count).ToList();
    }

    /// <summary>Clear the buffer (called when trace is toggled off).</summary>
    public void Clear() => _buffer.Clear();

    public class TraceLine
    {
        public string Timestamp { get; set; } = "";
        public string Level     { get; set; } = "";
        public string Message   { get; set; } = "";
    }
}
