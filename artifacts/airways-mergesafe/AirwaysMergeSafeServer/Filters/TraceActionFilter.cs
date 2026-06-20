using AirwaysMergeSafeServer.Infrastructure;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Diagnostics;

namespace AirwaysMergeSafeServer.Filters;

/// <summary>
/// Global action filter — automatically traces ENTER/EXIT/ERROR for every
/// controller action method across all controllers without any per-controller changes.
///
/// Registered globally in Program.cs via opts.Filters.Add&lt;TraceActionFilter&gt;().
/// Writes to the TraceLogger file configured at startup.
/// </summary>
public class TraceActionFilter : IActionFilter, IExceptionFilter
{
    private const string SwKey = "__trace_sw";

    // ── IActionFilter ──────────────────────────────────────────────────

    /// <summary>Called immediately before the action method executes.</summary>
    public void OnActionExecuting(ActionExecutingContext context)
    {
        try
        {
            var module = context.Controller.GetType().Name;
            var method = context.ActionDescriptor.RouteValues.TryGetValue("action", out var a) ? a : "Unknown";
            var args   = string.Join(", ",
                context.ActionArguments.Select(kv =>
                    $"{kv.Key}={Truncate(kv.Value?.ToString())}"));

            TraceLogger.Enter(module, method ?? "Unknown", args.Length > 0 ? args : null);

            // Store stopwatch so OnActionExecuted can report elapsed time
            context.HttpContext.Items[SwKey] = Stopwatch.StartNew();
        }
        catch { /* filter must never propagate exceptions */ }
    }

    /// <summary>Called after the action method executes (including on exception).</summary>
    public void OnActionExecuted(ActionExecutedContext context)
    {
        try
        {
            var module  = context.Controller.GetType().Name;
            var method  = context.ActionDescriptor.RouteValues.TryGetValue("action", out var a) ? a : "Unknown";
            var elapsed = context.HttpContext.Items.TryGetValue(SwKey, out var sw) && sw is Stopwatch s
                        ? $"{s.ElapsedMilliseconds}ms" : "?ms";

            if (context.Exception != null)
                TraceLogger.Error(module, method ?? "Unknown", context.Exception);
            else
            {
                var status = context.HttpContext.Response.StatusCode;
                TraceLogger.Exit(module, method ?? "Unknown", $"HTTP {status} in {elapsed}");
            }
        }
        catch { }
    }

    // ── IExceptionFilter ───────────────────────────────────────────────

    /// <summary>Called when an unhandled exception propagates up through the filter pipeline.</summary>
    public void OnException(ExceptionContext context)
    {
        try
        {
            var module = context.ActionDescriptor.RouteValues.TryGetValue("controller", out var c) ? c : "Unknown";
            var method = context.ActionDescriptor.RouteValues.TryGetValue("action",     out var a) ? a : "Unknown";
            TraceLogger.Error(module ?? "Unknown", method ?? "Unknown", context.Exception);
        }
        catch { }
    }

    private static string Truncate(string? s, int max = 120)
        => s == null ? "null" : s.Length <= max ? s : s[..max] + "…";
}
