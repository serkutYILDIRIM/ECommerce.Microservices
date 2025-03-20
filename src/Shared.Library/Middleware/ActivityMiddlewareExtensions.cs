using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Shared.Library.Telemetry.Contexts;

namespace Shared.Library.Middleware;

/// <summary>
/// Provides extension methods for configuring Activity-related middleware
/// </summary>
public static class ActivityMiddlewareExtensions
{
    /// <summary>
    /// Adds services required for async context propagation
    /// </summary>
    public static IServiceCollection AddAsyncContextPropagation(this IServiceCollection services)
    {
        // This method is primarily a marker for documentation,
        // as we rely on the built-in capabilities of Activity and execution context.
        // Any future enhancements to async context would be added here.
        
        return services;
    }

    /// <summary>
    /// Configures middleware for ensuring Activity context flows correctly in async operations
    /// </summary>
    public static IApplicationBuilder UseActivityContextPropagation(this IApplicationBuilder app)
    {
        // Add middleware to ensure Activity context is properly set up
        app.Use(async (context, next) =>
        {
            // Ensure we have an Activity even if one wasn't created by OpenTelemetry
            var activity = System.Diagnostics.Activity.Current;
            if (activity == null)
            {
                // Create a temporary activity to ensure context propagation works
                using var tempActivity = new System.Diagnostics.ActivitySource("Shared.Library")
                    .StartActivity("HttpRequest");
                
                await next();
            }
            else
            {
                // Ensure the activity has the request path set for better debugging
                if (!activity.Tags.Any(t => t.Key == "http.target"))
                {
                    activity.SetTag("http.target", context.Request.Path);
                }

                await next();
            }
        });
        
        return app;
    }
}
