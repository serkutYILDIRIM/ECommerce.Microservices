using System.Diagnostics;
using System.Net.Http;

namespace Shared.Library.Telemetry;

/// <summary>
/// Handles propagation of trace context and baggage across service boundaries.
/// </summary>
public class HttpClientContextPropagator
{
    private readonly string _serviceName;

    public HttpClientContextPropagator(string serviceName)
    {
        _serviceName = serviceName;
    }

    /// <summary>
    /// Enriches an outgoing HTTP request with tracing information
    /// </summary>
    public void EnrichRequest(HttpRequestMessage request, Activity? currentActivity = null)
    {
        if (request == null) return;

        currentActivity ??= Activity.Current;
        if (currentActivity == null) return;

        // Ensure trace ID is propagated
        var traceId = currentActivity.TraceId.ToString();
        var spanId = currentActivity.SpanId.ToString();

        // Add correlation ID header if not already present
        if (!request.Headers.Contains("x-correlation-id"))
        {
            request.Headers.Add("x-correlation-id", traceId);
        }

        // Ensure we propagate the caller service name for proper attribution
        if (!request.Headers.Contains("x-source-service"))
        {
            request.Headers.Add("x-source-service", _serviceName);
        }

        // Copy over baggage items as custom headers for further enrichment
        foreach (var baggageItem in currentActivity.Baggage)
        {
            var headerName = $"baggage-{baggageItem.Key}";
            if (!request.Headers.Contains(headerName))
            {
                request.Headers.Add(headerName, baggageItem.Value);
            }
        }
    }
}
