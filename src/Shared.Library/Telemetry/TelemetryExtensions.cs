using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Shared.Library.Telemetry.Exporters;
using Shared.Library.Telemetry.Processors;
using Shared.Library.Telemetry.Sampling;
using System.Diagnostics;

namespace Shared.Library.Telemetry;

public static class TelemetryExtensions
{
    public static IServiceCollection AddServiceTelemetry(
        this IServiceCollection services,
        string serviceName,
        string serviceVersion,
        Action<SamplingConfiguration>? configureSampling = null,
        TracerProviderType tracerType = TracerProviderType.AspNet,
        Action<CustomExporterOptions>? customExporterConfigure = null)
    {
        // Set up sampling configuration
        var samplingConfig = SamplingConfiguration.CreateDefault();
        configureSampling?.Invoke(samplingConfig);
        
        // Register sampling configuration
        services.AddSingleton(samplingConfig);
        
        // Register custom exporter services
        services.AddCustomExporterServices(serviceName, customExporterConfigure);

        // Basic OpenTelemetry setup
        var resourceBuilder = ResourceBuilder.CreateDefault()
            .AddService(serviceName, serviceVersion: serviceVersion);

        // Determine environment
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
        
        // Configure the OpenTelemetry TraceProvider
        services.AddOpenTelemetry()
            .WithTracing(builder =>
            {
                builder
                    .SetResourceBuilder(resourceBuilder)
                    .AddSource(serviceName); // Add the service's ActivitySource
                
                // Configure the sampler
                services.AddSingleton<ILogger<ConditionalSampler>>(sp => 
                    sp.GetRequiredService<ILoggerFactory>().CreateLogger<ConditionalSampler>());
                    
                builder.SetSampler(sp => samplingConfig.CreateSampler(
                    sp.GetRequiredService<ILogger<ConditionalSampler>>()));
                
                // Basic instrumentations
                builder.AddAspNetCoreInstrumentation(options =>
                    {
                        options.RecordException = true;
                        options.EnrichWithHttpRequest = (activity, request) =>
                        {
                            activity.SetTag("http.original_url", request.GetDisplayUrl());
                        };
                        options.EnrichWithHttpResponse = (activity, response) =>
                        {
                            if (response.StatusCode >= 400)
                            {
                                activity.SetStatus(ActivityStatusCode.Error, $"HTTP {response.StatusCode}");
                            }
                        };
                    })
                    .AddHttpClientInstrumentation(options =>
                    {
                        options.RecordException = true;
                        options.EnrichWithHttpRequestMessage = (activity, request) =>
                        {
                            if (request.RequestUri != null)
                            {
                                activity.SetTag("http.request.uri", request.RequestUri.ToString());
                            }
                        };
                        options.EnrichWithHttpResponseMessage = (activity, response) =>
                        {
                            if (response.StatusCode >= System.Net.HttpStatusCode.BadRequest)
                            {
                                activity.SetStatus(ActivityStatusCode.Error, $"HTTP {(int)response.StatusCode}");
                            }
                        };
                    })
                    .AddEntityFrameworkCoreInstrumentation(options =>
                    {
                        options.SetDbStatementForText = true;
                        options.SetDbStatementForStoredProcedure = true;
                    })
                    // Add custom exporter
                    .AddCustomExporter(serviceName, customExporterConfigure)
                    .AddConsoleExporter(); // For debugging
                
                // Add custom processors based on the tracer type
                ConfigureCustomProcessors(builder, services, serviceName, serviceVersion, environment, tracerType);
                
                // Add exporters from configuration
                ConfigureExporters(builder, services);
            });

        return services;
    }
    
    /// <summary>
    /// Configures and adds custom span processors to the trace provider
    /// </summary>
    private static void ConfigureCustomProcessors(
        TracerProviderBuilder builder,
        IServiceCollection services, 
        string serviceName, 
        string serviceVersion, 
        string environment,
        TracerProviderType tracerType)
    {
        // Ensure we have HttpContextAccessor for web applications
        if (tracerType == TracerProviderType.AspNet)
        {
            services.AddHttpContextAccessor();
        }
        
        // Add a processor creation delegate to the service collection
        services.AddSingleton<Func<IServiceProvider, BaseProcessor<Activity>>>(sp =>
        {
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            
            if (tracerType == TracerProviderType.AspNet)
            {
                var httpContextAccessor = sp.GetRequiredService<IHttpContextAccessor>();
                return CustomSpanProcessorFactory.CreateWebSpanProcessor(
                    serviceName, serviceVersion, environment, httpContextAccessor, loggerFactory);
            }
            else
            {
                return CustomSpanProcessorFactory.CreateWorkerSpanProcessor(
                    serviceName, serviceVersion, environment, loggerFactory);
            }
        });
        
        // Add the processor to the builder using the factory
        builder.AddProcessor(sp => sp.GetRequiredService<Func<IServiceProvider, BaseProcessor<Activity>>>()(sp));
    }
    
    // Helper to sanitize connection string for security
    private static string sanitizeConnectionString(string? connectionString)
    {
        if (string.IsNullOrEmpty(connectionString))
            return "unknown";
            
        // Replace password and other sensitive info with ***
        if (connectionString.Contains("Password=") || connectionString.Contains("pwd="))
        {
            return "sanitized";
        }
            
        return connectionString;
    }
    
    // ...existing code for ConfigureExporters...
}

/// <summary>
/// Defines the type of tracer provider to configure
/// </summary>
public enum TracerProviderType
{
    AspNet,
    Worker
}
