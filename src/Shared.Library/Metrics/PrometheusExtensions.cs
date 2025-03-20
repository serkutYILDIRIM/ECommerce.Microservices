using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using System;

namespace Shared.Library.Metrics;

/// <summary>
/// Extensions to configure Prometheus metrics endpoints
/// </summary>
public static class PrometheusExtensions
{
    /// <summary>
    /// Adds Prometheus scraping endpoint configuration to an ASP.NET Core application
    /// </summary>
    public static IServiceCollection AddPrometheusMetrics(
        this IServiceCollection services,
        string serviceName,
        string serviceVersion)
    {
        // Configure OpenTelemetry Prometheus exporter
        services.ConfigureOpenTelemetryMeterProvider(
            (sp, builder) => builder.AddPrometheusExporter(options =>
            {
                options.ScrapeEndpointPath = "/metrics";
                options.ScrapeResponseCacheDurationMilliseconds = 0; // Don't cache metrics
            }));

        // Add existing metrics services
        services.AddMetrics(serviceName, serviceVersion);
        
        // Add service info metrics
        services.AddSingleton<IStartupFilter>(
            new ServiceInfoMetricsFilter(serviceName, serviceVersion));
        
        return services;
    }
    
    /// <summary>
    /// Maps the Prometheus metrics endpoint in the application
    /// </summary>
    public static IEndpointRouteBuilder MapPrometheusScrapingEndpoint(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPrometheusScrapingEndpoint();
        
        return endpoints;
    }
}

/// <summary>
/// Startup filter to add service information metrics on application startup
/// </summary>
internal class ServiceInfoMetricsFilter : IStartupFilter
{
    private readonly string _serviceName;
    private readonly string _serviceVersion;
    
    public ServiceInfoMetricsFilter(string serviceName, string serviceVersion)
    {
        _serviceName = serviceName;
        _serviceVersion = serviceVersion;
    }
    
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            // Add middleware to expose service information as metrics
            app.Use(async (context, next) =>
            {
                // Expose service information as labels in a metric
                // This helps with service discovery and version tracking
                using var meter = new System.Diagnostics.Metrics.Meter("service.info");
                var serviceInfoGauge = meter.CreateObservableGauge(
                    "service_info",
                    () => new[] { new System.Diagnostics.Metrics.Measurement<int>(1) },
                    description: "Service information");
                    
                var tags = new System.Collections.Generic.KeyValuePair<string, object?>[]
                {
                    new("service.name", _serviceName),
                    new("service.version", _serviceVersion),
                    new("hostname", Environment.MachineName),
                    new("os.type", Environment.OSVersion.Platform.ToString()),
                    new("runtime", System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription)
                };
                
                // Continue the pipeline
                await next(context);
            });
            
            // Call the next middleware
            next(app);
        };
    }
}
