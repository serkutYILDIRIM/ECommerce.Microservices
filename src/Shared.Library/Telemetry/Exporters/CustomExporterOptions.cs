namespace Shared.Library.Telemetry.Exporters;

/// <summary>
/// Configuration options for the custom span exporter
/// </summary>
public class CustomExporterOptions
{
    /// <summary>
    /// External HTTP endpoint to send span data to (optional)
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>
    /// Whether to skip internal spans to reduce noise
    /// </summary>
    public bool SkipInternalSpans { get; set; } = true;

    /// <summary>
    /// Operation names to include even if they are internal spans
    /// </summary>
    public HashSet<string> IncludeOperationNames { get; set; } = new();

    /// <summary>
    /// Whether to include baggage items in exported span data
    /// </summary>
    public bool IncludeBaggage { get; set; } = true;

    /// <summary>
    /// Default service name to use if not specified in the span
    /// </summary>
    public string? DefaultServiceName { get; set; }

    /// <summary>
    /// Whether to enable custom processing logic
    /// </summary>
    public bool EnableCustomProcessing { get; set; } = true;

    /// <summary>
    /// Whether to enrich spans with additional metadata
    /// </summary>
    public bool EnrichWithMetadata { get; set; } = true;

    /// <summary>
    /// Threshold in milliseconds for considering a span "slow"
    /// </summary>
    public double SlowSpanThresholdMs { get; set; } = 1000;

    /// <summary>
    /// Maximum number of spans to store in memory
    /// </summary>
    public int MaxStoredSpans { get; set; } = 10000;

    /// <summary>
    /// Creates configuration with default values
    /// </summary>
    public static CustomExporterOptions Default(string serviceName)
    {
        return new CustomExporterOptions
        {
            DefaultServiceName = serviceName,
            IncludeOperationNames = new HashSet<string>
            {
                "Microsoft.AspNetCore.Hosting.HttpRequestIn",
                "Microsoft.AspNetCore.Server.Kestrel",
                "System.Net.Http.HttpRequestOut",
                "Microsoft.EntityFrameworkCore"
            }
        };
    }

    /// <summary>
    /// Add operation names to include even if they're internal spans
    /// </summary>
    public CustomExporterOptions WithOperationNames(params string[] operationNames)
    {
        foreach (var name in operationNames)
        {
            IncludeOperationNames.Add(name);
        }
        return this;
    }
}
