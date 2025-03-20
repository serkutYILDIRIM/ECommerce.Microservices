using Serilog.Core;
using Serilog.Events;
using System.Diagnostics;

namespace Shared.Library.Logging;

/// <summary>
/// Enriches log events with OpenTelemetry baggage items
/// </summary>
public class BaggageEnricher : ILogEventEnricher
{
    private readonly bool _includeAllBaggageItems;
    private readonly string[] _allowedBaggageKeys;

    /// <summary>
    /// Creates a new baggage enricher
    /// </summary>
    /// <param name="includeAllBaggageItems">Whether to include all baggage items or just specific ones</param>
    /// <param name="allowedBaggageKeys">If not including all items, the list of allowed keys</param>
    public BaggageEnricher(bool includeAllBaggageItems = true, string[]? allowedBaggageKeys = null)
    {
        _includeAllBaggageItems = includeAllBaggageItems;
        _allowedBaggageKeys = allowedBaggageKeys ?? Array.Empty<string>();
    }

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var activity = Activity.Current;
        if (activity == null)
            return;

        // Get all baggage items from the current activity
        var baggageItems = activity.Baggage.ToList();
        if (!baggageItems.Any())
            return;

        // Create a dictionary to hold the baggage items
        var baggage = new Dictionary<string, string?>();

        // Add all baggage items or just the allowed ones
        foreach (var item in baggageItems)
        {
            if (_includeAllBaggageItems || _allowedBaggageKeys.Contains(item.Key))
            {
                baggage[item.Key] = item.Value;
            }
        }

        // Add the baggage property to the log event
        if (baggage.Count > 0)
        {
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("Baggage", baggage, destructureObjects: true));
        }

        // Add the most common baggage items as top-level properties for easier filtering
        if (activity.Baggage.TryGetValue("service.name", out var serviceName))
        {
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("CallingService", serviceName));
        }

        if (activity.Baggage.TryGetValue("transaction.id", out var transactionId))
        {
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("TransactionId", transactionId));
        }

        if (activity.Baggage.TryGetValue("correlation.id", out var correlationId))
        {
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("CorrelationId", correlationId));
        }
    }
}

/// <summary>
/// Extension methods for Serilog configuration
/// </summary>
public static class BaggageLoggerConfigurationExtensions
{
    /// <summary>
    /// Enriches log events with OpenTelemetry baggage items
    /// </summary>
    public static LoggerConfiguration WithBaggage(
        this Serilog.Configuration.LoggerEnrichmentConfiguration enrichmentConfiguration,
        bool includeAllBaggageItems = true,
        string[]? allowedBaggageKeys = null)
    {
        return enrichmentConfiguration.With(new BaggageEnricher(includeAllBaggageItems, allowedBaggageKeys));
    }
}
