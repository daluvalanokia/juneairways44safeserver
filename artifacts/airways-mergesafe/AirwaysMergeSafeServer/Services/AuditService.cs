using AirwaysMergeSafeServer.Data;
using AirwaysMergeSafeServer.Models;

namespace AirwaysMergeSafeServer.Services;

/// <summary>
/// E2: AuditService — scoped service that writes AuditLog records.
///     Injected into controllers; populates UserId/HighwayId from HttpContext session.
///     Fire-and-forget: errors are logged but never surface to the user.
///
/// Usage in controllers:
///   await _audit.LogAsync("MergeZones", "Create", "MergeZone", model.Id.ToString(),
///       $"Created zone {model.ZoneName} on {model.HighwayId}");
/// </summary>
public class AuditService
{
    private readonly AppDbContext                _db;
    private readonly IHttpContextAccessor        _http;
    private readonly ILogger<AuditService>       _logger;

    public AuditService(
        AppDbContext                db,
        IHttpContextAccessor        http,
        ILogger<AuditService>       logger)
    {
        _db     = db;
        _http   = http;
        _logger = logger;
    }

    public async Task LogAsync(
        string  controller,
        string  action,
        string? entityType = null,
        string? entityId   = null,
        string? summary    = null)
    {
        try
        {
            var ctx       = _http.HttpContext;
            var userId    = ctx?.Session.GetString("UserId")    ?? "system";
            var fullName  = ctx?.Session.GetString("FullName")  ?? "System";
            var highwayId = ctx?.Session.GetString("HighwayId");
            var ip        = ctx?.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            var entry = new AuditLog
            {
                UserId      = userId[..Math.Min(50, userId.Length)],
                FullName    = fullName[..Math.Min(100, fullName.Length)],
                HighwayId   = highwayId,
                Controller  = controller,
                Action      = action,
                EntityType  = entityType,
                EntityId    = entityId,
                Summary     = summary?[..Math.Min(500, summary.Length)],
                IpAddress   = ip,
                CreatedDate = DateTime.UtcNow
            };

            _db.AuditLogs.Add(entry);
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // Audit failure must never break the primary operation
            _logger.LogError(ex,
                "AuditService: Failed to write audit record for {Controller}.{Action}", controller, action);
        }
    }
}
