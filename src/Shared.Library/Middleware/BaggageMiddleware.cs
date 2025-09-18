using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Shared.Library.Telemetry.Baggage;
using System.Diagnostics;

namespace Shared.Library.Middleware;

/// <summary>
/// Middleware for handling OpenTelemetry baggage in HTTP requests and responses
/// </summary>
public class BaggageMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<BaggageMiddleware> _logger;
    private readonly string _serviceName;
    private readonly string _serviceVersion;

    // Standard correlation headers to extract
    private static readonly string[] CorrelationHeaders = new[]
    {
        "X-Correlation-ID",
        "X-Request-ID",
        "Request-ID",
        "Correlation-ID",
        "traceparent",
        "X-B3-TraceId"
    };

    // Headers that should be propagated as baggage
    private static readonly string[] HeadersToPropagateAsBaggage = new[]
    {
        "X-Customer-ID",
        "X-Order-ID",
        "X-User-ID",
        "X-Business-Unit",
        "X-Channel",
        "X-Region",
        "X-Tenant-ID"
    };

    public BaggageMiddleware(
        RequestDelegate next,
        ILogger<BaggageMiddleware> logger,
        string serviceName,
        string serviceVersion)
    {
        _next = next;
        _logger = logger;
        _serviceName = serviceName;
        _serviceVersion = serviceVersion;
    }

    public async Task InvokeAsync(HttpContext context, BaggageManager baggageManager)
    {
        var activity = Activity.Current;
        if (activity == null)
        {
            _logger.LogDebug("No activity present for baggage extraction");
            await _next(context);
            return;
        }

        try
        {
            // First extract any correlation ID
            ExtractCorrelationId(context, baggageManager);

            // Extract baggage from HTTP headers
            ExtractBaggageFromHeaders(context, baggageManager);

            // Extract baggage from standard W3C header
            ExtractBaggageFromW3CHeader(context, baggageManager);

            // Always add service information
            baggageManager.Set(BaggageManager.Keys.ServiceName, _serviceName);
            baggageManager.Set(BaggageManager.Keys.ServiceVersion, _serviceVersion);
            baggageManager.Set(BaggageManager.Keys.ServiceInstance, Environment.MachineName);

            // Add request source if available
            if (context.Request.Headers.TryGetValue("X-Source-Service", out var sourceService) &&
                !string.IsNullOrEmpty(sourceService))
            {
                baggageManager.Set(BaggageManager.Keys.RequestSource, sourceService);
            }

            // Copy baggage to tags for local visibility
            baggageManager.CopyBaggageToTags();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error processing baggage from request");
        }

        // Continue with the request
        await _next(context);

        try
        {
            // For specific endpoints, propagate baggage back in response headers
            if (ShouldPropagateBaggageInResponse(context))
            {
                PropagateBaggageInResponseHeaders(context, baggageManager);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error propagating baggage to response");
        }
    }

    private void ExtractCorrelationId(HttpContext context, BaggageManager baggageManager)
    {
        // Check for correlation ID in various headers
        foreach (var headerName in CorrelationHeaders)
        {
            if (context.Request.Headers.TryGetValue(headerName, out var correlationId) &&
                !string.IsNullOrEmpty(correlationId))
            {
                baggageManager.Set(BaggageManager.Keys.CorrelationId, correlationId);
                _logger.LogTrace("Extracted correlation ID {CorrelationId} from header {HeaderName}",
                    correlationId, headerName);
                return;
            }
        }

        // If no correlation ID found, generate one
        var newCorrelationId = Guid.NewGuid().ToString();
        baggageManager.Set(BaggageManager.Keys.CorrelationId, newCorrelationId);
        _logger.LogTrace("Generated new correlation ID {CorrelationId}", newCorrelationId);
    }

    private void ExtractBaggageFromHeaders(HttpContext context, BaggageManager baggageManager)
    {
        // Extract business context from specific headers
        foreach (var headerName in HeadersToPropagateAsBaggage)
        {
            if (context.Request.Headers.TryGetValue(headerName, out var headerValue) &&
                !string.IsNullOrEmpty(headerValue))
            {
                // Convert header name to baggage key (X-Customer-ID -> customer.id)
                var baggageKey = ConvertHeaderNameToBaggageKey(headerName);
                baggageManager.Set(baggageKey, headerValue);
                _logger.LogTrace("Extracted baggage {Key}={Value} from header {HeaderName}",
                    baggageKey, headerValue, headerName);
            }
        }

        // Special handling for customer tier if available
        if (context.Request.Headers.TryGetValue("X-Customer-Tier", out var customerTier) &&
            !string.IsNullOrEmpty(customerTier))
        {
            baggageManager.Set(BaggageManager.Keys.CustomerTier, customerTier);
        }

        // Special handling for order priority
        if (context.Request.Headers.TryGetValue("X-Order-Priority", out var orderPriority) &&
            !string.IsNullOrEmpty(orderPriority))
        {
            baggageManager.Set(BaggageManager.Keys.OrderPriority, orderPriority);
        }
    }

    private void ExtractBaggageFromW3CHeader(HttpContext context, BaggageManager baggageManager)
    {
        // The W3C baggage header contains comma-separated key-value pairs
        if (context.Request.Headers.TryGetValue("baggage", out var baggageHeader) &&
            !string.IsNullOrEmpty(baggageHeader))
        {
            var baggageItems = baggageHeader.ToString().Split(',');

            foreach (var item in baggageItems)
            {
                var keyValue = item.Trim().Split('=');
                if (keyValue.Length == 2)
                {
                    baggageManager.Set(keyValue[0].Trim(), keyValue[1].Trim());
                    _logger.LogTrace("Extracted baggage {Key}={Value} from W3C baggage header",
                        keyValue[0].Trim(), keyValue[1].Trim());
                }
            }
        }
    }

    private bool ShouldPropagateBaggageInResponse(HttpContext context)
    {
        // Propagate for API endpoints, but not for static files
        var path = context.Request.Path.ToString().ToLowerInvariant();
        return path.StartsWith("/api/") && !path.Contains(".") && context.Response.StatusCode < 500;
    }

    private void PropagateBaggageInResponseHeaders(HttpContext context, BaggageManager baggageManager)
    {
        // Add correlation ID to response headers
        var correlationId = baggageManager.Get(BaggageManager.Keys.CorrelationId);
        if (!string.IsNullOrEmpty(correlationId))
        {
            context.Response.Headers["X-Correlation-ID"] = correlationId;
        }

        // Add transaction ID if available
        var transactionId = baggageManager.Get(BaggageManager.Keys.TransactionId);
        if (!string.IsNullOrEmpty(transactionId))
        {
            context.Response.Headers["X-Transaction-ID"] = transactionId;
        }

        // We could add additional business context, but be careful not to expose sensitive information
    }

    /// <summary>
    /// Converts HTTP header names to baggage keys (e.g., X-Customer-ID -> customer.id)
    /// </summary>
    private string ConvertHeaderNameToBaggageKey(string headerName)
    {
        // Remove X- prefix
        var key = headerName.StartsWith("X-", StringComparison.OrdinalIgnoreCase)
            ? headerName.Substring(2)
            : headerName;

        // Split by hyphens and convert to lowercase
        var parts = key.Split('-');
        return string.Join(".", parts).ToLowerInvariant();
    }
}

/// <summary>
/// Extension methods for using the BaggageMiddleware
/// </summary>
public static class BaggageMiddlewareExtensions
{
    /// <summary>
    /// Adds middleware for handling OpenTelemetry baggage
    /// </summary>
    public static IApplicationBuilder UseBaggagePropagation(
        this IApplicationBuilder app,
        string serviceName,
        string serviceVersion)
    {
        return app.UseMiddleware<BaggageMiddleware>(serviceName, serviceVersion);
    }
}
