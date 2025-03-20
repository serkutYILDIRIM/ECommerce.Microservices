using Microsoft.AspNetCore.Http;
using Serilog.Context;
using System.Diagnostics;
using System.Text;

namespace Shared.Library.Middleware;

/// <summary>
/// Middleware to enrich log context with trace information and request details
/// </summary>
public class LogEnrichmentMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string _serviceName;
    private readonly ILogger<LogEnrichmentMiddleware> _logger;

    public LogEnrichmentMiddleware(RequestDelegate next, string serviceName, ILogger<LogEnrichmentMiddleware> logger)
    {
        _next = next;
        _serviceName = serviceName;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Get current activity for trace context
        var activity = Activity.Current;
        
        // Add important request info to the active span
        if (activity != null)
        {
            activity.SetTag("http.method", context.Request.Method);
            activity.SetTag("http.url", GetDisplayUrl(context.Request));
            activity.SetTag("http.host", context.Request.Host.Value);
            activity.SetTag("http.request_id", context.TraceIdentifier);
            
            // Add user info if available
            if (context.User?.Identity?.IsAuthenticated == true)
            {
                activity.SetTag("user.id", context.User.Identity.Name);
                activity.SetTag("enduser.id", context.User.Identity.Name);
            }
        }
        
        // Get or create correlation ID
        string correlationId = context.Request.Headers["x-correlation-id"].FirstOrDefault() ?? 
                              activity?.TraceId.ToString() ?? 
                              Guid.NewGuid().ToString();
        
        // Get baggage from headers or current activity
        Dictionary<string, string> baggage = ExtractBaggage(context, activity, correlationId);

        // Push correlation identifiers into the log context
        using (LogContext.PushProperty("TraceId", activity?.TraceId.ToString() ?? "unknown"))
        using (LogContext.PushProperty("SpanId", activity?.SpanId.ToString() ?? "unknown"))
        using (LogContext.PushProperty("ParentSpanId", activity?.ParentSpanId.ToString() ?? "unknown"))
        using (LogContext.PushProperty("CorrelationId", correlationId))
        using (LogContext.PushProperty("RequestPath", context.Request.Path))
        using (LogContext.PushProperty("RequestMethod", context.Request.Method))
        using (LogContext.PushProperty("UserAgent", context.Request.Headers["User-Agent"].ToString()))
        using (LogContext.PushProperty("RequestHost", context.Request.Host.Value))
        using (LogContext.PushProperty("ServiceName", _serviceName))
        using (LogContext.PushProperty("Baggage", baggage))
        {
            try
            {
                // Continue processing the request
                await _next(context);
                
                // Add response info
                LogContext.PushProperty("StatusCode", context.Response.StatusCode);
                LogContext.PushProperty("ResponseTime", GetElapsedTime(activity));
                
                // Log request completion
                LogRequestCompletion(context, activity);
            }
            catch (Exception ex)
            {
                // Add exception details to the span
                if (activity != null)
                {
                    activity.SetStatus(ActivityStatusCode.Error, ex.Message);
                    activity.RecordException(ex);
                }
                
                // Log the exception with all context
                _logger.LogError(ex, "Request failed: {Method} {Path}", 
                    context.Request.Method, context.Request.Path);
                
                // Re-throw to allow error handling middleware to process it
                throw;
            }
        }
    }
    
    private static string GetDisplayUrl(HttpRequest request)
    {
        var displayUrl = new StringBuilder(request.Scheme)
            .Append("://")
            .Append(request.Host.Value);
        
        if (request.Path.HasValue)
            displayUrl.Append(request.Path.Value);
        
        if (request.QueryString.HasValue)
            displayUrl.Append(request.QueryString.Value);
        
        return displayUrl.ToString();
    }
    
    private static Dictionary<string, string> ExtractBaggage(HttpContext context, Activity? activity, string correlationId)
    {
        var baggage = new Dictionary<string, string>();
        
        // Add baggage from current activity
        if (activity != null)
        {
            foreach (var item in activity.Baggage)
            {
                baggage[item.Key] = item.Value;
            }
        }
        
        // Add correlation ID to baggage
        if (!baggage.ContainsKey("correlation.id"))
        {
            baggage["correlation.id"] = correlationId;
        }
        
        // Add service name to baggage
        if (!baggage.ContainsKey("service.name") && context.Request.Headers.TryGetValue("x-source-service", out var sourceService))
        {
            baggage["service.name"] = sourceService.ToString();
        }
        
        // Extract any baggage headers from the request
        if (context.Request.Headers.TryGetValue("baggage", out var baggageHeader))
        {
            foreach (var pair in ParseW3CBaggageHeader(baggageHeader.ToString()))
            {
                baggage[pair.Key] = pair.Value;
            }
        }
        
        // Also look for HTTP_BAGGAGE header (some proxies transform it)
        if (context.Request.Headers.TryGetValue("HTTP_BAGGAGE", out var httpBaggage))
        {
            foreach (var pair in ParseW3CBaggageHeader(httpBaggage.ToString()))
            {
                baggage[pair.Key] = pair.Value;
            }
        }
        
        return baggage;
    }
    
    private static Dictionary<string, string> ParseW3CBaggageHeader(string baggageHeader)
    {
        var result = new Dictionary<string, string>();
        
        if (string.IsNullOrEmpty(baggageHeader))
            return result;
            
        // Split baggage items (comma-separated list)
        var baggageItems = baggageHeader.Split(',');
        
        foreach (var item in baggageItems)
        {
            // Each baggage item is in key=value format
            var keyValue = item.Trim().Split('=', 2);
            if (keyValue.Length == 2)
            {
                result[keyValue[0].Trim()] = keyValue[1].Trim();
            }
        }
        
        return result;
    }
    
    private static double GetElapsedTime(Activity? activity)
    {
        if (activity == null)
            return 0;
            
        return (DateTime.UtcNow - activity.StartTimeUtc).TotalMilliseconds;
    }
    
    private void LogRequestCompletion(HttpContext context, Activity? activity)
    {
        var statusCode = context.Response.StatusCode;
        var level = statusCode >= 500 ? LogLevel.Error : 
                    statusCode >= 400 ? LogLevel.Warning : 
                    LogLevel.Information;
                    
        var responseTime = GetElapsedTime(activity);
        
        if (level == LogLevel.Error)
        {
            _logger.Log(level, "HTTP {Method} {Path} responded {StatusCode} in {ResponseTime:0.0000}ms", 
                context.Request.Method, context.Request.Path, statusCode, responseTime);
        }
        else if (level == LogLevel.Warning)
        {
            _logger.Log(level, "HTTP {Method} {Path} responded {StatusCode} in {ResponseTime:0.0000}ms", 
                context.Request.Method, context.Request.Path, statusCode, responseTime);
        }
        else if (responseTime > 500) // Log slow requests
        {
            _logger.LogWarning("HTTP {Method} {Path} responded {StatusCode} in {ResponseTime:0.0000}ms (slow)", 
                context.Request.Method, context.Request.Path, statusCode, responseTime);
        }
        else if (level == LogLevel.Debug)
        {
            _logger.Log(level, "HTTP {Method} {Path} responded {StatusCode} in {ResponseTime:0.0000}ms", 
                context.Request.Method, context.Request.Path, statusCode, responseTime);
        }
    }
}

// Extension method to add the middleware
public static class LogEnrichmentMiddlewareExtensions
{
    public static IApplicationBuilder UseLogEnrichment(this IApplicationBuilder app, string serviceName)
    {
        return app.UseMiddleware<LogEnrichmentMiddleware>(serviceName);
    }
}
