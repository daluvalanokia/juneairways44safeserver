namespace AirwaysMergeSafeServer.Filters;

/// <summary>
/// Marker attribute. When present on a controller or action,
/// SessionAuthFilter skips authentication for that route.
/// Used by HealthController so load balancers can reach /health without a session.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class SkipSessionAuthAttribute : Attribute { }
