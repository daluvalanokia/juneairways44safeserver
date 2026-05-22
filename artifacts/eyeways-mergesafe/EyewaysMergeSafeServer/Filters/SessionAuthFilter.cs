using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Security.Cryptography;
using System.Text;

namespace EyewaysMergeSafeServer.Filters;

/// <summary>
/// Global action filter — enforces session authentication for all routes except:
///   • Portal / Home controllers (login page)
/// All /api/* routes (GET and POST) require a valid session or X-Device-Token header.
/// </summary>
public class SessionAuthFilter : IActionFilter
{
    private static readonly HashSet<string> _publicControllers =
        new(StringComparer.OrdinalIgnoreCase) { "Portal", "Home" };

    private readonly ILogger<SessionAuthFilter> _logger;

    public SessionAuthFilter(ILogger<SessionAuthFilter> logger)
    {
        _logger = logger;
    }

    public void OnActionExecuting(ActionExecutingContext context)
    {
        var ctrl   = context.RouteData.Values["controller"]?.ToString() ?? "";
        var path   = context.HttpContext.Request.Path;
        var method = context.HttpContext.Request.Method;
        var ip     = context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        if (_publicControllers.Contains(ctrl)) return;

        if (path.StartsWithSegments("/api"))
        {
            if (HasValidSession(context) || HasValidDeviceToken(context)) return;
            _logger.LogWarning("Security: 401 Unauthorized — path={Path} method={Method} ip={Ip}", path, method, ip);
            context.Result = new UnauthorizedResult();
            return;
        }

        if (!HasValidSession(context))
        {
            _logger.LogWarning("Security: 401 Redirect — path={Path} method={Method} ip={Ip}", path, method, ip);
            context.Result = new RedirectToActionResult("Index", "Portal", null);
        }
    }

    public void OnActionExecuted(ActionExecutedContext context) { }

    private static bool HasValidSession(ActionExecutingContext ctx)
        => !string.IsNullOrEmpty(ctx.HttpContext.Session.GetString("HighwayId"));

    private static bool HasValidDeviceToken(ActionExecutingContext ctx)
    {
        var token = ctx.HttpContext.Request.Headers["X-Device-Token"].ToString();
        if (string.IsNullOrEmpty(token)) return false;

        var cfg          = ctx.HttpContext.RequestServices.GetService<IConfiguration>();
        var configuredKey = cfg?["DeviceApiKey"];

        if (string.IsNullOrEmpty(configuredKey)) return false;

        var tokenBytes = Encoding.UTF8.GetBytes(token);
        var keyBytes   = Encoding.UTF8.GetBytes(configuredKey);
        return CryptographicOperations.FixedTimeEquals(tokenBytes, keyBytes);
    }
}
