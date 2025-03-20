using OpenTelemetry;
using System.Diagnostics;

namespace Shared.Library.Telemetry.Processors;

/// <summary>
/// Custom processor for enriching OpenTelemetry spans
/// </summary>
/// <remarks>
/// Implements BaseProcessor to hook into Activity lifecycle for span enrichment
/// </remarks>
public class CustomSpanProcessor : BaseProcessor<Activity>
{
    private readonly IEnumerable<ISpanEnricher> _enrichers;
    private readonly ILogger<CustomSpanProcessor> _logger;

    /// <summary>
    /// Creates a new instance of the CustomSpanProcessor
    /// </summary>
    public CustomSpanProcessor(IEnumerable<ISpanEnricher> enrichers, ILogger<CustomSpanProcessor> logger)
    {
        _enrichers = enrichers;
        _logger = logger;
    }

    /// <summary>
    /// Called when a span is started. Enriches the span with initial attributes.
    /// </summary>
    public override void OnStart(Activity activity)
    {
        try
        {
            // Apply all enrichers at span start
            foreach (var enricher in _enrichers)
            {
                enricher.EnrichSpanAtStart(activity);
            }
        }
        catch (Exception ex)
        {
            // Don't let enrichment failures impact telemetry collection
            _logger.LogError(ex, "Error during span start enrichment for {OperationName}", activity.OperationName);
        }
    }

    /// <summary>
    /// Called when a span ends. Enriches the span with final attributes and metrics.
    /// </summary>
    public override void OnEnd(Activity activity)
    {
        try
        {
            // Apply all enrichers at span end
            foreach (var enricher in _enrichers)
            {
                enricher.EnrichSpanAtEnd(activity);
            }
        }
        catch (Exception ex)
        {
            // Don't let enrichment failures impact telemetry collection
            _logger.LogError(ex, "Error during span end enrichment for {OperationName}", activity.OperationName);
        }
    }
}
