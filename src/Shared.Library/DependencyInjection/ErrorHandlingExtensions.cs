using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shared.Library.Middleware;
using System.Diagnostics;

namespace Shared.Library.DependencyInjection;

/// <summary>
/// Extension methods for configuring error handling
/// </summary>
public static class ErrorHandlingExtensions
{
    /// <summary>
    /// Adds services required for error handling and exception tracking
    /// </summary>
    public static IServiceCollection AddErrorHandling(this IServiceCollection services)
    {
        // Register any services needed for error handling

        // Configure OpenTelemetry exception processor if needed

        return services;
    }

    /// <summary>
    /// Configures the application to use global exception handling with OpenTelemetry integration
    /// </summary>
    public static IApplicationBuilder UseErrorHandling(this IApplicationBuilder app)
    {
        // Add the global exception handling middleware
        app.UseGlobalExceptionHandler();

        return app;
    }

    /// <summary>
    /// Configures the application with comprehensive exception handling, 
    /// combining global exception handling and problem details
    /// </summary>
    public static IApplicationBuilder UseComprehensiveErrorHandling(this IApplicationBuilder app)
    {
        // Add the global exception handling middleware
        app.UseGlobalExceptionHandler();

        // Use built-in problem details
        app.UseStatusCodePages();

        return app;
    }

    /// <summary>
    /// Sets up global exception handling for ASP.NET Core 7+ minimal APIs
    /// </summary>
    public static WebApplication UseMinimalApiErrorHandling(this WebApplication app)
    {
        // Add error handling middleware
        app.UseGlobalExceptionHandler();

        // Add global error handler for minimal APIs
        app.Use(async (context, next) =>
        {
            try
            {
                await next(context);
            }
            catch (Exception ex)
            {
                // If exception wasn't already handled by middleware, record it
                Activity.Current?.SetStatus(ActivityStatusCode.Error);
                Activity.Current?.AddEvent(new ActivityEvent("unhandled_exception",
                    tags: new ActivityTagsCollection
                    {
                        { "exception.type", ex.GetType().FullName },
                        { "exception.message", ex.Message },
                        { "exception.stacktrace", ex.StackTrace }
                    }
                ));

                // Log and rethrow to let the ExceptionHandlingMiddleware handle it
                var logger = context.RequestServices.GetService<ILogger<WebApplication>>();
                logger?.LogError(ex, "Unhandled exception in minimal API endpoint");

                throw;
            }
        });

        return app;
    }
}
