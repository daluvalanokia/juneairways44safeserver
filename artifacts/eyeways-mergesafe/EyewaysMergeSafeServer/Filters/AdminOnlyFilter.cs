using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace EyewaysMergeSafeServer.Filters;

/// <summary>
/// Restricts an action or controller to admin users only.
/// Returns 403 Forbidden for authenticated non-admin users.
/// Unauthenticated callers are handled by SessionAuthFilter (redirect/401).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public class AdminOnlyAttribute : Attribute, IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
        var userType = context.HttpContext.Session.GetString("UserType");
        if (userType != "admin")
        {
            context.Result = new ForbidResult();
        }
    }

    public void OnActionExecuted(ActionExecutedContext context) { }
}
