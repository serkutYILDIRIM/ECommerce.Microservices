using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Trace;

namespace Shared.Library.Telemetry.Exporters;

/// <summary>
/// Extension methods for configuring the custom span exporter
/// </summary>
public static class CustomExporterExtensions
{
    /// <summary>
    /// Adds the custom span exporter to the trace provider builder
    /// </summary>
    public static TracerProviderBuilder AddCustomExporter(
        this TracerProviderBuilder builder,
        string serviceName,
        Action<CustomExporterOptions>? configure = null)
    {
        // Create default options
        var options = CustomExporterOptions.Default(serviceName);
        
        // Apply any custom configuration
        configure?.Invoke(options);
        
        // Register options and storage
        builder.AddCustomExporter(options);
        
        return builder;
    }
    
    /// <summary>
    /// Adds the custom span exporter with the specified options
    /// </summary>
    public static TracerProviderBuilder AddCustomExporter(
        this TracerProviderBuilder builder,
        CustomExporterOptions options)
    {
        // Register services in the DI container
        var services = builder as IServiceCollection;
        if (services != null)
        {
            services.AddSingleton(options);
            services.AddSingleton<TelemetryStorage>(sp => 
                new TelemetryStorage(
                    sp.GetRequiredService<ILogger<TelemetryStorage>>(), 
                    options.MaxStoredSpans));
            
            services.AddSingleton<CustomSpanExporter>();
        }
        
        // Add the exporter to the trace provider
        builder.AddProcessor(sp => sp.GetRequiredService<CustomSpanExporter>());
        
        return builder;
    }
    
    /// <summary>
    /// Configures and registers services needed for the custom exporter
    /// </summary>
    public static IServiceCollection AddCustomExporterServices(
        this IServiceCollection services,
        string serviceName,
        Action<CustomExporterOptions>? configure = null)
    {
        // Create default options
        var options = CustomExporterOptions.Default(serviceName);
        
        // Apply any custom configuration
        configure?.Invoke(options);
        
        // Register services
        services.AddSingleton(options);
        services.AddSingleton<TelemetryStorage>(sp => 
            new TelemetryStorage(
                sp.GetRequiredService<ILogger<TelemetryStorage>>(), 
                options.MaxStoredSpans));
        
        services.AddSingleton<CustomSpanExporter>();
        
        return services;
    }
}
