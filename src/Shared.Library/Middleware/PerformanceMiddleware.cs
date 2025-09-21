using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Shared.Library.Metrics;
using System.Diagnostics;

namespace Shared.Library.Middleware;

/// <summary>
/// Middleware to capture API endpoint performance metrics
/// </summary>
public class PerformanceMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<PerformanceMiddleware> _logger;

    public PerformanceMiddleware(RequestDelegate next, ILogger<PerformanceMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, PerformanceMetrics metrics)
    {
        // Skip metrics endpoints to avoid recursion
        if (context.Request.Path.StartsWithSegments("/metrics") ||
            context.Request.Path.StartsWithSegments("/health"))
        {
            await _next(context);
            return;
        }

        var path = context.Request.Path.Value ?? "";
        var method = context.Request.Method;
        var endpoint = $"{method} {path}";

        // Start timing
        var sw = Stopwatch.StartNew();
        var originalBodyStream = context.Response.Body;

        try
        {
            // Call the next middleware in the pipeline
            await _next(context);

            sw.Stop();

            // Record metrics
            metrics.RecordHttpRequestDuration(
                sw.ElapsedMilliseconds,
                method,
                path,
                context.Response.StatusCode);

            metrics.RecordApiEndpointPerformance(
                endpoint,
                sw.ElapsedMilliseconds,
                context.Response.StatusCode < 400,
                context.Response.StatusCode);

            // Log slow requests
            if (sw.ElapsedMilliseconds > 1000)
            {
                _logger.LogWarning("Slow request: {Method} {Path} took {ElapsedMs}ms with status {StatusCode}",
                    method, path, sw.ElapsedMilliseconds, context.Response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            sw.Stop();

            // Record error metrics
            metrics.RecordHttpRequestDuration(
                sw.ElapsedMilliseconds,
                method,
                path,
                500); // Assume 500 for uncaught exceptions

            metrics.RecordApiEndpointPerformance(
                endpoint,
                sw.ElapsedMilliseconds,
                false,
                500);

            _logger.LogError(ex, "Error executing request: {Method} {Path}", method, path);

            throw; // Re-throw to maintain error handling flow
        }
    }
}

// Extension method to add middleware
public static class PerformanceMiddlewareExtensions
{
    public static IApplicationBuilder UsePerformanceMonitoring(this IApplicationBuilder app)
    {
        return app.UseMiddleware<PerformanceMiddleware>();
    }
}
