using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Shared.Library.Logging;
using Shared.Library.Metrics;
using Shared.Library.Telemetry.Baggage;
using Shared.Library.Telemetry.Exporters;
using Shared.Library.Telemetry.Processors;
using Shared.Library.Telemetry.Sampling;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Shared.Library.Telemetry
{
    /// <summary>
    /// Core OpenTelemetry configuration and extension methods for the application.
    /// Provides methods to configure tracing, metrics, logging, and resource detection.
    /// </summary>
    public static class TelemetryExtensions
    {
        /// <summary>
        /// Adds OpenTelemetry services to the service collection with comprehensive configuration
        /// for tracing, metrics, and resource detection.
        /// </summary>
        /// <param name="services">The IServiceCollection to add services to</param>
        /// <param name="configuration">Application configuration for telemetry settings</param>
        /// <param name="serviceName">The name of the service for telemetry attribution</param>
        /// <param name="serviceVersion">The version of the service</param>
        /// <returns>The service collection for chaining</returns>
        public static IServiceCollection AddOpenTelemetryServices(
            this IServiceCollection services,
            IConfiguration configuration,
            string serviceName,
            string serviceVersion,
            Action<TelemetryOptions> configure)
        {
            // Create a TelemetryOptions instance and apply configurations
            var options = new TelemetryOptions();
            configure?.Invoke(options);

            // Create a shared resource builder that identifies the service
            // This information will be attached to all telemetry data (traces, metrics, logs)
            var resourceBuilder = ResourceBuilder.CreateDefault()
                .AddService(serviceName: serviceName, serviceVersion: serviceVersion)
                .AddTelemetrySdk()
                .AddEnvironmentVariableDetector(); // Automatically detect environment info

            // Register ActivitySource for manual instrumentation
            // This allows creating custom spans in application code
            services.AddSingleton(new ActivitySource(serviceName));

            // Register Meter for custom metrics
            // This allows creating custom metrics in application code
            services.AddSingleton(new Meter(serviceName));

            // Add and configure OpenTelemetry Tracing
            services.AddOpenTelemetry()
                .WithTracing(builder =>
                {
                    builder
                        // Apply the common resource information to all spans
                        .SetResourceBuilder(resourceBuilder)

                        // Configure OpenTelemetry instrumentation for common libraries
                        // These auto-instruments provide spans for common operations without manual code
                        .AddHttpClientInstrumentation(opts =>
                        {
                            // Enrich spans with additional context from HTTP calls
                            opts.EnrichWithHttpRequestMessage = (activity, request) =>
                            {
                                activity.SetTag("http.request.header.x-correlation-id",
                                    request.Headers.Contains("x-correlation-id") ?
                                        request.Headers.GetValues("x-correlation-id").FirstOrDefault() : "");
                            };

                            opts.EnrichWithHttpResponseMessage = (activity, response) =>
                            {
                                // Add response size information for performance analysis
                                if (response.Content?.Headers?.ContentLength != null)
                                {
                                    activity.SetTag("http.response.content_length", response.Content.Headers.ContentLength);
                                }
                            };

                            // Filter out health check endpoints to reduce noise
                            opts.FilterHttpRequestMessage = (request) =>
                            {
                                return !request.RequestUri.PathAndQuery.Contains("/health");
                            };
                        })
                        .AddAspNetCoreInstrumentation(opts =>
                        {
                            // Record HTTP request body for specific content types when debugging
                            opts.RecordException = true;

                            // Filter out health check and metrics endpoints
                            opts.Filter = ctx =>
                                !ctx.Request.Path.StartsWithSegments("/health") &&
                                !ctx.Request.Path.StartsWithSegments("/metrics");

                            // Enrich spans with additional HTTP context
                            opts.EnrichWithHttpRequest = (activity, request) =>
                            {
                                activity.SetTag("http.request.host", request.Host.Value);
                                activity.SetTag("http.request.path", request.Path);
                                activity.SetTag("http.request.query", request.QueryString.Value);
                            };
                        })
                        // Auto-instrument EF Core and SQL operations
                        .AddEntityFrameworkCoreInstrumentation(opts =>
                        {
                            // Set to true for development to see full SQL queries
                            opts.SetDbStatementForText = true;
                        })
                        // Add exporters - how telemetry data gets sent
                        .AddOtlpExporter(opts =>
                        {
                            // Get OTLP endpoint from configuration or use default
                            opts.Endpoint = new Uri(configuration.GetValue<string>("OpenTelemetry:OtlpEndpoint") ?? "http://localhost:4317");
                        })
                        // Optional console exporter for debugging
                        .AddConsoleExporter()
                        // Apply the custom sampling configuration
                        .AddCustomSamplers(configuration, serviceName, options);

                    // Add any custom processors for spans
                    // These processors can modify spans before they're exported
                    builder.AddCustomSpanProcessors();
                });

            // Rest of the method remains the same...

            return services;
        }

        // Modify this extension method to accept the options
        public static TracerProviderBuilder AddCustomSamplers(
            this TracerProviderBuilder builder,
            IConfiguration configuration,
            string serviceName,
            TelemetryOptions options)
        {
            // Configure sampler based on options
            switch (options.SamplerType)
            {
                case SamplerType.AlwaysOn:
                    builder.SetSampler(new AlwaysOnSampler());
                    break;

                case SamplerType.AlwaysOff:
                    builder.SetSampler(new AlwaysOffSampler());
                    break;

                case SamplerType.TraceIdRatio:
                    builder.SetSampler(new TraceIdRatioBasedSampler(options.SamplingProbability));
                    break;

                case SamplerType.ParentBased:
                    builder.SetSampler(
                        new ParentBasedSampler(
                            new TraceIdRatioBasedSampler(options.SamplingProbability)));
                    break;

                default:
                    // Default to always on
                    builder.SetSampler(new AlwaysOnSampler());
                    break;
            }

            // Apply composite sampling if enabled and there are rules
            if (options.UseCompositeSampling && options.Rules.Count > 0)
            {
                // You'll need to implement a composite sampler that uses the rules
                // This is just a placeholder - you'll need to create a real implementation
                // builder.SetSampler(new CompositeSampler(options.Rules, options.MaxTracesPerSecond));
            }

            return builder;
        }


        // Additional extension methods...
    }

    public static class CustomSpanProcessorExtensions
    {
        public static TracerProviderBuilder AddCustomSpanProcessors(this TracerProviderBuilder builder)
        {
            // Add custom span processors here
            // Example: builder.AddProcessor(new CustomSpanProcessor());

            return builder;
        }
    }
}
