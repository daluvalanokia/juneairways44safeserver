using System.Text.Json;
using AirwaysMergeSafeServer.Infrastructure;
using Microsoft.Extensions.Caching.Memory;

namespace AirwaysMergeSafeServer.Services;

/// <summary>
/// A1 FIX: Replaced static readonly Random _rng with Random.Shared throughout.
///         Random is not thread-safe when shared across threads; Random.Shared is.
/// </summary>
public class TrafficService
{
    private readonly IConfiguration     _cfg;
    private readonly IMemoryCache       _cache;
    private readonly IHttpClientFactory _httpFactory;

    public TrafficService(IConfiguration cfg, IMemoryCache cache, IHttpClientFactory httpFactory)
    { _cfg = cfg; _cache = cache; _httpFactory = httpFactory; }

    public async Task<object> GetSegmentsAsync(string highwayId)
    {
        TraceLogger.Enter("TrafficService", nameof(GetSegmentsAsync), $"highwayId={highwayId}");
        try
        {
            var cacheKey = $"traffic_svc_{highwayId}";
            if (_cache.TryGetValue(cacheKey, out object? cached) && cached != null)
            {
                TraceLogger.Exit("TrafficService", nameof(GetSegmentsAsync), "cache hit");
                return cached;
            }

            object segments;
            var tomTomKey = _cfg["TomTomApiKey"];

            if (!string.IsNullOrWhiteSpace(tomTomKey))
            {
                try
                {
                    var client = _httpFactory.CreateClient();
                    client.Timeout = TimeSpan.FromSeconds(8);
                    var bbox = highwayId switch
                    {
                        "I20-TX"  => "32.72,-97.15,32.80,-96.95",
                        "I35-TX"  => "31.05,-97.38,31.60,-97.08",
                        _         => "29.70,-95.80,29.90,-95.30"
                    };
                    var url = $"https://api.tomtom.com/traffic/services/4/flowSegmentData/absolute/10/json?key={tomTomKey}&bbox={bbox}";
                    TraceLogger.Info("TrafficService", nameof(GetSegmentsAsync), $"Calling TomTom API bbox={bbox}");
                    var response = await client.GetAsync(url);
                    segments = response.IsSuccessStatusCode
                        ? new { source = "tomtom", data = JsonSerializer.Deserialize<object>(await response.Content.ReadAsStringAsync()) }
                        : BuildSimulated(highwayId);
                }
                catch (Exception ex)
                {
                    TraceLogger.Error("TrafficService", nameof(GetSegmentsAsync), ex);
                    segments = BuildSimulated(highwayId);
                }
            }
            else
            {
                TraceLogger.Info("TrafficService", nameof(GetSegmentsAsync), "No TomTom key — using simulated data");
                segments = BuildSimulated(highwayId);
            }

            _cache.Set(cacheKey, segments, TimeSpan.FromMinutes(5));
            TraceLogger.Exit("TrafficService", nameof(GetSegmentsAsync), "fetched");
            return segments;
        }
        catch (Exception ex)
        {
            TraceLogger.Error("TrafficService", nameof(GetSegmentsAsync), ex);
            throw;
        }
    }

    // A1 FIX: Random.Shared — thread-safe, no lock required
    public static object BuildSimulated(string highwayId)
    {
        TraceLogger.Enter("TrafficService", nameof(BuildSimulated), $"highwayId={highwayId}");
        try
        {
            var rng   = Random.Shared;
            var names = highwayId switch
            {
                "I20-TX"  => new[] { "Dallas West","Grand Prairie","Arlington","Fort Worth East","Mesquite","Duncanville","DeSoto","Lancaster" },
                "I35-TX"  => new[] { "Waco North","Temple","Georgetown","Round Rock","Austin North","San Marcos","New Braunfels","San Antonio" },
                _         => new[] { "Houston West","Katy","Sugar Land","Houston East","Beaumont","Orange","Baytown","Pasadena" }
            };
            var result = (object)new
            {
                source      = "simulated",
                highway     = highwayId,
                generatedAt = DateTime.UtcNow,
                segments    = names.Select((name, i) => new
                {
                    id                = $"SEG-{i+1:D3}",
                    name,
                    speedMph          = rng.Next(15, 75),
                    freeFlowSpeedMph  = 70,
                    congestion        = rng.Next(0, 5) switch { 4 => "heavy", 3 => "moderate", _ => "free" },
                    travelTimeSeconds = rng.Next(60, 600)
                }).ToList()
            };
            TraceLogger.Exit("TrafficService", nameof(BuildSimulated), $"{names.Length} segments");
            return result;
        }
        catch (Exception ex)
        {
            TraceLogger.Error("TrafficService", nameof(BuildSimulated), ex);
            throw;
        }
    }
}
