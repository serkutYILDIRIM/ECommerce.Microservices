using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;

namespace Shared.Library.Metrics;

public static class MetricsExtensions
{
    /// <summary>
    /// Adds common metric recording components to the service collection
    /// </summary>
    public static IServiceCollection AddMetrics(
        this IServiceCollection services,
        string serviceName,
        string serviceVersion)
    {
        // Register the MeterProvider with a singleton lifecycle
        services.AddSingleton(sp => new MeterProvider(serviceName, serviceVersion));
        
        // Register PerformanceMetrics service
        services.AddSingleton<PerformanceMetrics>();
        
        return services;
    }
}

/// <summary>
/// Provides meters for creating metrics
/// </summary>
public class MeterProvider
{
    public Meter AppMeter { get; }
    
    public MeterProvider(string serviceName, string serviceVersion)
    {
        // Create a meter with the service name as the name and version as the version
        AppMeter = new Meter(serviceName, serviceVersion);
    }
}
