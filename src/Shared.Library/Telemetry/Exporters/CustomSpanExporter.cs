using Microsoft.Extensions.Logging;
using OpenTelemetry;
using System.Diagnostics;
using System.Text.Json;

namespace Shared.Library.Telemetry.Exporters;

/// <summary>
/// Custom exporter for OpenTelemetry spans that provides additional processing capabilities
/// </summary>
public class CustomSpanExporter : BaseExporter<Activity>
{
    private readonly ILogger<CustomSpanExporter> _logger;
    private readonly CustomExporterOptions _options;
    private readonly TelemetryStorage _storage;

    public CustomSpanExporter(
        CustomExporterOptions options, 
        TelemetryStorage storage,
        ILogger<CustomSpanExporter> logger)
    {
        _options = options;
        _storage = storage;
        _logger = logger;
        _logger.LogInformation("CustomSpanExporter initialized with endpoint: {Endpoint}", 
            _options.Endpoint ?? "Not configured");
    }

    /// <summary>
    /// Export a batch of telemetry items
    /// </summary>
    public override ExportResult Export(in Batch<Activity> batch)
    {
        try
        {
            foreach (var span in batch)
            {
                // Skip internal spans if configured to do so
                if (_options.SkipInternalSpans && span.Kind == ActivityKind.Internal && 
                    !_options.IncludeOperationNames.Contains(span.OperationName))
                {
                    continue;
                }

                // Create a span record with essential information
                var spanRecord = new SpanRecord
                {
                    TraceId = span.TraceId.ToString(),
                    SpanId = span.SpanId.ToString(),
                    ParentSpanId = span.ParentSpanId.ToString(),
                    Name = span.DisplayName,
                    Kind = span.Kind.ToString(),
                    StartTime = span.StartTimeUtc.Date, // Convert DateTimeOffset to DateTime
                    EndTime = (span.StartTimeUtc + span.Duration).Date, // Correct way to calculate end time
                    Duration = span.Duration.TotalMilliseconds,
                    Status = span.Status.ToString(),
                    Tags = ExtractTags(span),
                    Events = ExtractEvents(span),
                    ServiceName = GetServiceName(span)
                };

                // Store span record for retrieval
                _storage.AddSpan(spanRecord);

                // If external endpoint is configured, send data there
                if (!string.IsNullOrEmpty(_options.Endpoint))
                {
                    _ = SendToEndpointAsync(spanRecord);
                }

                // Apply custom processing logic
                if (_options.EnableCustomProcessing)
                {
                    ApplyCustomProcessing(span, spanRecord);
                }
            }

            return ExportResult.Success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting spans in CustomSpanExporter");
            return ExportResult.Failure;
        }
    }

    /// <summary>
    /// Extract tags from the span
    /// </summary>
    private Dictionary<string, object> ExtractTags(Activity span)
    {
        var tags = new Dictionary<string, object>();
        
        foreach (var tag in span.Tags)
        {
            tags[tag.Key] = tag.Value ?? string.Empty;
        }
        
        // Add baggage items if enabled
        if (_options.IncludeBaggage)
        {
            foreach (var item in span.Baggage)
            {
                tags[$"baggage.{item.Key}"] = item.Value ?? string.Empty;
            }
        }
        
        return tags;
    }

    /// <summary>
    /// Extract events from the span
    /// </summary>
    private List<SpanEvent> ExtractEvents(Activity span)
    {
        var events = new List<SpanEvent>();

        foreach (var activityEvent in span.Events)
        {
            var eventTags = new Dictionary<string, object>();

            foreach (var tag in activityEvent.Tags)
            {
                eventTags[tag.Key] = tag.Value?.ToString() ?? string.Empty;
            }

            events.Add(new SpanEvent
            {
                Name = activityEvent.Name,
                Timestamp = activityEvent.Timestamp.DateTime, // Convert DateTimeOffset to DateTime
                Tags = eventTags
            });
        }

        return events;
    }

    /// <summary>
    /// Get service name from span attributes or baggage
    /// </summary>
    /// <summary>
    /// Get service name from span attributes or baggage
    /// </summary>
    private string GetServiceName(Activity span)
    {
        // Convert span.Tags to a dictionary
        var tagsDictionary = span.Tags.ToDictionary(t => t.Key, t => t.Value);

        // Try to get service name from span attributes
        if (tagsDictionary.TryGetValue("service.name", out var serviceNameTag) ||
            tagsDictionary.TryGetValue("service", out serviceNameTag) ||
            tagsDictionary.TryGetValue("component", out serviceNameTag))
        {
            if (!string.IsNullOrEmpty(serviceNameTag))
            {
                return serviceNameTag;
            }
        }

        // Convert span.Baggage to a dictionary
        var baggageDictionary = span.Baggage.ToDictionary(b => b.Key, b => b.Value);

        // Try to get from baggage
        if (baggageDictionary.TryGetValue("service.name", out var serviceName) &&
            !string.IsNullOrEmpty(serviceName))
        {
            return serviceName;
        }

        // Fallback to default
        return _options.DefaultServiceName ?? "unknown-service";
    }

    /// <summary>
    /// Send span data to external endpoint if configured
    /// </summary>
    private async Task SendToEndpointAsync(SpanRecord span)
    {
        if (string.IsNullOrEmpty(_options.Endpoint))
            return;
            
        try
        {
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(5);
            
            var content = new StringContent(
                JsonSerializer.Serialize(span), 
                System.Text.Encoding.UTF8, 
                "application/json");
                
            var response = await httpClient.PostAsync(_options.Endpoint, content);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to send span data to endpoint: {StatusCode}", 
                    response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending span data to endpoint: {Endpoint}", _options.Endpoint);
        }
    }

    /// <summary>
    /// Apply custom processing logic to spans
    /// </summary>
    private void ApplyCustomProcessing(Activity span, SpanRecord spanRecord)
    {
        // This can be extended with your own custom processing logic
        // For instance, you might want to enrich spans with additional information,
        // trigger alerts for certain conditions, or send data to custom analytics platforms
        
        if (_options.EnrichWithMetadata)
        {
            // Example: add environment info
            spanRecord.Tags["custom.environment"] = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
            spanRecord.Tags["custom.process_id"] = Environment.ProcessId;
            spanRecord.Tags["custom.machine_name"] = Environment.MachineName;
        }
        
        // Detect errors
        if (span.Status == ActivityStatusCode.Error)
        {
            _storage.AddErrorSpan(spanRecord);
            
            // You could trigger alerts here or send notifications
            _logger.LogWarning("Error span detected: {SpanName}, TraceId: {TraceId}", 
                span.DisplayName, span.TraceId);
        }
        
        // Detect slow spans
        if (span.Duration.TotalMilliseconds > _options.SlowSpanThresholdMs)
        {
            _storage.AddSlowSpan(spanRecord);
            
            _logger.LogWarning("Slow span detected: {SpanName}, Duration: {Duration}ms, TraceId: {TraceId}", 
                span.DisplayName, span.Duration.TotalMilliseconds, span.TraceId);
        }
    }

    /// <summary>
    /// Shutdown the exporter
    /// </summary>
    protected override bool OnShutdown(int timeoutMilliseconds)
    {
        _logger.LogInformation("CustomSpanExporter shutting down");
        return true;
    }
}

/// <summary>
/// Represents a processed span record
/// </summary>
public class SpanRecord
{
    public string TraceId { get; set; } = string.Empty;
    public string SpanId { get; set; } = string.Empty;
    public string ParentSpanId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public double Duration { get; set; }
    public string Status { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public Dictionary<string, object> Tags { get; set; } = new();
    public List<SpanEvent> Events { get; set; } = new();
}

/// <summary>
/// Represents a span event
/// </summary>
public class SpanEvent
{
    public string Name { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public Dictionary<string, object> Tags { get; set; } = new();
}
