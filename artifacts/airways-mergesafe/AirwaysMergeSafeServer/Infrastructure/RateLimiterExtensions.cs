using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

namespace AirwaysMergeSafeServer.Infrastructure;

/// <summary>
/// E3: Rate-limiting policies.
///
/// LoginPolicy    — 10 attempts per IP per 15 minutes on POST /Portal/Login.
///                  Matches the account-lockout window; provides network-level
///                  brute-force protection before BCrypt is even invoked.
///
/// IngestPolicy   — 60 device ingest calls per IP per minute on POST /api/events/ingest.
///                  Prevents a single device or attacker from flooding the event table.
///
/// ApiReadPolicy  — 120 read calls per IP per minute on GET /api/* endpoints.
///                  Prevents scraping / enumeration loops.
///
/// All policies use a fixed-window limiter; 429 responses include a
/// Retry-After header so clients can back off gracefully.
/// </summary>
public static class RateLimiterExtensions
{
    public const string LoginPolicy   = "login_policy";
    public const string IngestPolicy  = "ingest_policy";
    public const string ApiReadPolicy = "api_read_policy";

    public static IServiceCollection AddAppRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(opts =>
        {
            opts.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            opts.OnRejected = async (ctx, ct) =>
            {
                ctx.HttpContext.Response.Headers["Retry-After"] = "60";
                ctx.HttpContext.Response.ContentType = "application/json";
                await ctx.HttpContext.Response.WriteAsync(
                    "{\"error\":\"Too many requests. Please try again later.\"}", ct);
            };

            // ── Login: 10 requests per IP per 15 minutes ──────────────────
            opts.AddPolicy(LoginPolicy, ctx =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit          = 10,
                        Window               = TimeSpan.FromMinutes(15),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit           = 0
                    }));

            // ── Ingest: 60 per IP per minute ──────────────────────────────
            opts.AddPolicy(IngestPolicy, ctx =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit          = 60,
                        Window               = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit           = 0
                    }));

            // ── API reads: 120 per IP per minute ──────────────────────────
            opts.AddPolicy(ApiReadPolicy, ctx =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit          = 120,
                        Window               = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit           = 0
                    }));
        });

        return services;
    }
}
