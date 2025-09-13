using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Shared.Library.Metrics;

/// <summary>
/// Provides performance metrics for API endpoints, database operations, and system resources
/// </summary>
public class PerformanceMetrics
{
    private readonly Meter _meter;
    private readonly ILogger<PerformanceMetrics> _logger;

    // HTTP Request Metrics
    private readonly Histogram<double> _httpRequestDurationHistogram;
    private readonly Counter<long> _httpRequestCounter;
    private readonly Counter<long> _httpRequestErrorCounter;

    // Database Metrics
    private readonly Histogram<double> _dbOperationDurationHistogram;
    private readonly Counter<long> _dbOperationCounter;
    private readonly Counter<long> _dbOperationErrorCounter;

    // Resource Utilization Metrics
    private readonly Histogram<double> _cpuUtilizationHistogram;
    private readonly Histogram<double> _memoryUsageHistogram;
    private readonly Counter<long> _gcCollectionCounter;

    // API Performance metrics
    private readonly Dictionary<string, Histogram<double>> _endpointPerformanceHistograms = new();
    public static readonly string MeterName = "PerformanceMetrics";

    private readonly object _lock = new();

    public PerformanceMetrics(MeterProvider meterProvider, ILogger<PerformanceMetrics> logger)
    {
        _meter = meterProvider.AppMeter;
        _logger = logger;

        // HTTP Metrics
        _httpRequestDurationHistogram = _meter.CreateHistogram<double>(
            name: "performance.http.request.duration",
            unit: "ms",
            description: "Duration of HTTP requests");

        _httpRequestCounter = _meter.CreateCounter<long>(
            name: "performance.http.request.count",
            unit: "{requests}",
            description: "Number of HTTP requests");

        _httpRequestErrorCounter = _meter.CreateCounter<long>(
            name: "performance.http.request.errors",
            unit: "{errors}",
            description: "Number of HTTP request errors");

        // Database Metrics
        _dbOperationDurationHistogram = _meter.CreateHistogram<double>(
            name: "performance.db.operation.duration",
            unit: "ms",
            description: "Duration of database operations");

        _dbOperationCounter = _meter.CreateCounter<long>(
            name: "performance.db.operation.count",
            unit: "{operations}",
            description: "Number of database operations");

        _dbOperationErrorCounter = _meter.CreateCounter<long>(
            name: "performance.db.operation.errors",
            unit: "{errors}",
            description: "Number of database operation errors");

        // Resource Utilization Metrics
        _cpuUtilizationHistogram = _meter.CreateHistogram<double>(
            name: "performance.system.cpu_utilization",
            unit: "%",
            description: "CPU utilization percentage");

        _memoryUsageHistogram = _meter.CreateHistogram<double>(
            name: "performance.system.memory_usage",
            unit: "MB",
            description: "Memory usage in megabytes");

        _gcCollectionCounter = _meter.CreateCounter<long>(
            name: "performance.system.gc_collections",
            unit: "{collections}",
            description: "Number of garbage collections");

        _logger.LogInformation("Performance metrics initialized");

        // Start collecting resource utilization metrics
        StartResourceMonitoring();
    }

    #region HTTP Request Performance

    public void RecordHttpRequestDuration(double durationMs, string method, string path, int statusCode)
    {
        _httpRequestDurationHistogram.Record(durationMs,
            new KeyValuePair<string, object?>("http.method", method),
            new KeyValuePair<string, object?>("http.path", path),
            new KeyValuePair<string, object?>("http.status_code", statusCode));

        _httpRequestCounter.Add(1,
            new KeyValuePair<string, object?>("http.method", method),
            new KeyValuePair<string, object?>("http.path", path),
            new KeyValuePair<string, object?>("http.status_code", statusCode));

        // Track error rate
        if (statusCode >= 400)
        {
            _httpRequestErrorCounter.Add(1,
                new KeyValuePair<string, object?>("http.method", method),
                new KeyValuePair<string, object?>("http.path", path),
                new KeyValuePair<string, object?>("http.status_code", statusCode));
        }
    }

    public void RecordApiEndpointPerformance(string endpoint, double durationMs, bool success, int? statusCode = null)
    {
        // Create or get endpoint-specific histogram
        var histogram = GetEndpointHistogram(endpoint);

        var tags = new List<KeyValuePair<string, object?>>
        {
            new KeyValuePair<string, object?>("endpoint", endpoint),
            new KeyValuePair<string, object?>("success", success)
        };

        if (statusCode.HasValue)
        {
            tags.Add(new KeyValuePair<string, object?>("status_code", statusCode.Value));
        }

        histogram.Record(durationMs, tags.ToArray());
    }

    private Histogram<double> GetEndpointHistogram(string endpoint)
    {
        lock (_lock)
        {
            if (!_endpointPerformanceHistograms.TryGetValue(endpoint, out var histogram))
            {
                histogram = _meter.CreateHistogram<double>(
                    name: "performance.api.endpoint.duration",
                    unit: "ms",
                    description: "Duration of API endpoint execution");

                _endpointPerformanceHistograms[endpoint] = histogram;
            }

            return histogram;
        }
    }

    #endregion

    #region Database Performance

    public void RecordDbOperationDuration(double durationMs, string operationType, string? entityType = null, bool success = true)
    {
        var tags = new List<KeyValuePair<string, object?>>
        {
            new KeyValuePair<string, object?>("db.operation_type", operationType),
            new KeyValuePair<string, object?>("db.success", success)
        };

        if (!string.IsNullOrEmpty(entityType))
        {
            tags.Add(new KeyValuePair<string, object?>("db.entity_type", entityType));
        }

        _dbOperationDurationHistogram.Record(durationMs, tags.ToArray());

        _dbOperationCounter.Add(1, tags.ToArray());

        if (!success)
        {
            _dbOperationErrorCounter.Add(1, tags.ToArray());
        }
    }

    public void RecordEntityFrameworkOperation(string operation, string entityType, double durationMs, bool success)
    {
        RecordDbOperationDuration(durationMs, $"ef_core.{operation}", entityType, success);
    }

    #endregion

    #region System Resource Utilization

    private Process? _currentProcess;
    private PerformanceCounter? _cpuCounter;
    private Timer? _resourceMonitoringTimer;

    private void StartResourceMonitoring()
    {
        try
        {
            _currentProcess = Process.GetCurrentProcess();

            if (OperatingSystem.IsWindows())
            {
                try
                {
                    _cpuCounter = new PerformanceCounter("Process", "% Processor Time",
                        Process.GetCurrentProcess().ProcessName);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not create CPU performance counter. CPU monitoring will be limited.");
                }
            }

            // Sample every 5 seconds
            _resourceMonitoringTimer = new Timer(_ => SampleResourceUsage(), null, 0, 5000);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start resource monitoring");
        }
    }

    private void SampleResourceUsage()
    {
        try
        {
            if (_currentProcess != null)
            {
                // Refresh process info
                _currentProcess.Refresh();

                // Record memory usage
                var memoryUsageMb = _currentProcess.WorkingSet64 / (1024.0 * 1024.0);
                _memoryUsageHistogram.Record(memoryUsageMb,
                    new KeyValuePair<string, object?>("process.name", _currentProcess.ProcessName));

                // Record CPU usage 
                if (_cpuCounter != null)
                {
                    try
                    {
                        var cpuUsage = _cpuCounter.NextValue() / Environment.ProcessorCount;
                        _cpuUtilizationHistogram.Record(cpuUsage,
                            new KeyValuePair<string, object?>("process.name", _currentProcess.ProcessName));
                    }
                    catch { }
                }

                // Record GC collections
                int gen0Count = GC.CollectionCount(0);
                int gen1Count = GC.CollectionCount(1);
                int gen2Count = GC.CollectionCount(2);

                _gcCollectionCounter.Add(1,
                    new KeyValuePair<string, object?>("gc.generation", "gen0"),
                    new KeyValuePair<string, object?>("gc.count", gen0Count));

                _gcCollectionCounter.Add(1,
                    new KeyValuePair<string, object?>("gc.generation", "gen1"),
                    new KeyValuePair<string, object?>("gc.count", gen1Count));

                _gcCollectionCounter.Add(1,
                    new KeyValuePair<string, object?>("gc.generation", "gen2"),
                    new KeyValuePair<string, object?>("gc.count", gen2Count));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error collecting resource metrics");
        }
    }

    #endregion
}
