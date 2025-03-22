using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System.Diagnostics;
using System.Text;

namespace Shared.Library.Middleware;

public class TracingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string _serviceName;

    public TracingMiddleware(RequestDelegate next, string serviceName)
    {
        _next = next;
        _serviceName = serviceName;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Get or create a correlation ID
        string correlationId;
        if (!context.Request.Headers.TryGetValue("x-correlation-id", out var existingCorrelationId) || string.IsNullOrEmpty(existingCorrelationId))
        {
            correlationId = Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString();
            context.Request.Headers["x-correlation-id"] = correlationId;
        }
        else
        {
            correlationId = existingCorrelationId.ToString();
        }

        // Always add the correlation ID to the response
        context.Response.Headers["x-correlation-id"] = correlationId;

        // Add service name to response for debugging
        context.Response.Headers["x-processed-by"] = _serviceName;

        // Process baggage headers from the request
        var currentActivity = Activity.Current;
        if (currentActivity != null)
        {
            // Add standard baggage items
            currentActivity.AddBaggage("service.name", _serviceName);
            currentActivity.AddBaggage("correlation.id", correlationId);

            // Extract service chain for visualization of the request flow
            string serviceChain = "";
            if (context.Request.Headers.TryGetValue("x-source-service", out var sourceService) && !string.IsNullOrEmpty(sourceService))
            {
                serviceChain = $"{sourceService} -> {_serviceName}";
                currentActivity.AddBaggage("service.chain", serviceChain);
                currentActivity.SetTag("service.chain", serviceChain);
            }
            else
            {
                currentActivity.AddBaggage("service.chain", _serviceName);
                currentActivity.SetTag("service.chain", _serviceName);
            }

            // Add any baggage headers to the current activity
            foreach (var header in context.Request.Headers)
            {
                if (header.Key.StartsWith("baggage-"))
                {
                    string baggageKey = header.Key["baggage-".Length..];
                    currentActivity.AddBaggage(baggageKey, header.Value.ToString());
                    currentActivity.SetTag($"baggage.{baggageKey}", header.Value.ToString());
                }
            }

            // Set important trace context tags
            currentActivity.SetTag("service.name", _serviceName);
            currentActivity.SetTag("http.correlation_id", correlationId);
            
            if (!string.IsNullOrEmpty(serviceChain))
            {
                context.Response.Headers["x-service-chain"] = serviceChain;
            }
        }

        await _next(context);
    }
}

// Extension method to add middleware
public static class TracingMiddlewareExtensions
{
    public static IApplicationBuilder UseTracing(this IApplicationBuilder app, string serviceName)
    {
        return app.UseMiddleware<TracingMiddleware>(serviceName);
    }
}
