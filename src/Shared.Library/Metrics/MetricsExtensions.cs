using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using System.Diagnostics.Metrics;


namespace Shared.Library.Metrics
{
    /// <summary>
    /// Extensions for configuring and using metrics in the application.
    /// Provides methods for registering and initializing metrics providers.
    /// </summary>
    public static class MetricsExtensions
    {
        /// <summary>
        /// Adds metrics services to the service collection.
        /// This includes performance metrics, common business metrics, and custom meters.
        /// </summary>
        /// <param name="services">The service collection to add the metrics to</param>
        /// <param name="serviceName">The name of the service</param>
        /// <param name="serviceVersion">The version of the service</param>
        /// <returns>The service collection for chaining</returns>
        public static IServiceCollection AddMetrics(this IServiceCollection services, string serviceName, string serviceVersion)
        {
            // Register the MeterProvider with a singleton lifecycle
            services.AddSingleton(sp => new MeterProvider(serviceName, serviceVersion));

            // Register performance metrics singleton
            // These capture common performance indicators across all services
            services.AddSingleton<PerformanceMetrics>();

            // Add Prometheus metrics endpoint for scraping
            services.AddPrometheusMetrics();

            return services;
        }

        /// <summary>
        /// Adds Prometheus metrics endpoint for scraping.
        /// Configures the /metrics endpoint that Prometheus will scrape.
        /// </summary>
        /// <param name="services">The service collection to add Prometheus metrics to</param>
        /// <returns>The service collection for chaining</returns>
        private static IServiceCollection AddPrometheusMetrics(this IServiceCollection services)
        {
            // Add Prometheus exporter endpoint
            services.AddOpenTelemetry()
                .WithMetrics(builder => builder
                    .AddPrometheusExporter());

            return services;
        }

        /// <summary>
        /// Creates a Counter metric with appropriate key-value units.
        /// Counters track values that only increase (like total requests).
        /// </summary>
        /// <typeparam name="T">The numeric type for the counter</typeparam>
        /// <param name="meter">The meter to create the counter on</param>
        /// <param name="name">Name of the counter</param>
        /// <param name="unit">Unit of measurement</param>
        /// <param name="description">Description of what this counter measures</param>
        /// <returns>A counter metric instrument</returns>
        public static Counter<T> CreateCounter<T>(this Meter meter, string name, string unit, string description)
            where T : struct
        {
            // Create a counter with semantic naming convention and appropriate description
            return meter.CreateCounter<T>(name, unit, description);
        }

        /// <summary>
        /// Creates a Histogram metric with appropriate key-value units.
        /// Histograms track the distribution of measurements (like request duration).
        /// </summary>
        /// <typeparam name="T">The numeric type for the histogram</typeparam>
        /// <param name="meter">The meter to create the histogram on</param>
        /// <param name="name">Name of the histogram</param>
        /// <param name="unit">Unit of measurement</param>
        /// <param name="description">Description of what this histogram measures</param>
        /// <returns>A histogram metric instrument</returns>
        public static Histogram<T> CreateHistogram<T>(this Meter meter, string name, string unit, string description)
            where T : struct
        {
            // Create a histogram with semantic naming convention and appropriate description
            return meter.CreateHistogram<T>(name, unit, description);
        }

        /// <summary>
        /// Creates an UpDownCounter metric with appropriate key-value units.
        /// UpDownCounters track values that can increase or decrease (like active requests).
        /// </summary>
        /// <typeparam name="T">The numeric type for the up-down counter</typeparam>
        /// <param name="meter">The meter to create the up-down counter on</param>
        /// <param name="name">Name of the counter</param>
        /// <param name="unit">Unit of measurement</param>
        /// <param name="description">Description of what this counter measures</param>
        /// <returns>An up-down counter metric instrument</returns>
        public static UpDownCounter<T> CreateUpDownCounter<T>(this Meter meter, string name, string unit, string description)
            where T : struct
        {
            // Create an up-down counter with semantic naming convention and appropriate description
            return meter.CreateUpDownCounter<T>(name, unit, description);
        }

        /// <summary>
        /// Creates a Gauge metric instrument using Observable Gauge.
        /// Gauges capture a current value that can go up and down.
        /// </summary>
        /// <typeparam name="T">The numeric type for the gauge</typeparam>
        /// <param name="meter">The meter to create the gauge on</param>
        /// <param name="name">Name of the gauge</param>
        /// <param name="unit">Unit of measurement</param>
        /// <param name="description">Description of what this gauge measures</param>
        /// <param name="observeValue">Function to call for getting the current value</param>
        /// <param name="tags">Additional tags/dimensions for the metric</param>
        public static void CreateObservableGauge<T>(
            this Meter meter,
            string name,
            string unit,
            string description,
            Func<T> observeValue,
            params KeyValuePair<string, object>[] tags)
            where T : struct
        {
            // Register an observable gauge that calls the observer function to get current values
            meter.CreateObservableGauge(name, () => new Measurement<T>(observeValue(), tags), unit, description);
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
}
