using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;
using System.Diagnostics;

namespace Serilog.Enrichers.Span;

/// <summary>
/// Options for span enrichment
/// </summary>
public class SpanOptions
{
    public bool IncludeOperationName { get; set; } = true;
    public bool IncludeTags { get; set; } = false;
    public bool IncludeEvents { get; set; } = false;
    public bool IncludeTraceFlags { get; set; } = true;
    public bool IncludeLinks { get; set; } = false;
    public string[]? IncludeSpecificTags { get; set; } = null;
}

/// <summary>
/// Enriches log events with information from the current Activity (span)
/// </summary>
public class SpanEnricher : ILogEventEnricher
{
    private readonly SpanOptions _options;

    public SpanEnricher() : this(new SpanOptions()) { }

    public SpanEnricher(SpanOptions options)
    {
        _options = options;
    }

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var activity = Activity.Current;
        if (activity == null)
            return;

        // Add basic trace context
        AddTraceContext(logEvent, propertyFactory, activity);

        // Add operation name if requested
        if (_options.IncludeOperationName && !string.IsNullOrEmpty(activity.OperationName))
        {
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("OperationName", activity.OperationName));
        }

        // Add tags if requested
        if (_options.IncludeTags && activity.Tags.Any())
        {
            AddTags(logEvent, propertyFactory, activity);
        }

        // Add specific tags if requested
        if (_options.IncludeSpecificTags != null && _options.IncludeSpecificTags.Length > 0)
        {
            AddSpecificTags(logEvent, propertyFactory, activity);
        }

        // Add trace flags if requested
        if (_options.IncludeTraceFlags)
        {
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("TraceFlags", activity.ActivityTraceFlags.ToString()));
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("IsSampled", activity.Recorded));
        }

        // Add events if requested
        if (_options.IncludeEvents && activity.Events.Any())
        {
            AddEvents(logEvent, propertyFactory, activity);
        }

        // Add links if requested
        if (_options.IncludeLinks && activity.Links.Any())
        {
            AddLinks(logEvent, propertyFactory, activity);
        }
    }

    private void AddTraceContext(LogEvent logEvent, ILogEventPropertyFactory propertyFactory, Activity activity)
    {
        // Add TraceId and SpanId
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("TraceId", activity.TraceId.ToString()));
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("SpanId", activity.SpanId.ToString()));

        // Add ParentSpanId if it exists
        if (activity.ParentSpanId != default)
        {
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("ParentSpanId", activity.ParentSpanId.ToString()));
        }

        // Add TraceState if it exists
        if (!string.IsNullOrEmpty(activity.TraceStateString))
        {
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("TraceState", activity.TraceStateString));
        }

        // Add RootId if different from TraceId
        if (activity.RootId != activity.TraceId.ToString() && !string.IsNullOrEmpty(activity.RootId))
        {
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("RootId", activity.RootId));
        }
    }

    private void AddTags(LogEvent logEvent, ILogEventPropertyFactory propertyFactory, Activity activity)
    {
        var tags = new Dictionary<string, object?>();
        foreach (var tag in activity.Tags)
        {
            tags[tag.Key] = tag.Value;
        }

        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("Tags", tags, destructureObjects: true));
    }

    private void AddSpecificTags(LogEvent logEvent, ILogEventPropertyFactory propertyFactory, Activity activity)
    {
        foreach (var key in _options.IncludeSpecificTags!)
        {
            var tag = activity.Tags.FirstOrDefault(t => t.Key == key);
            if (!string.IsNullOrEmpty(tag.Key))
            {
                logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty($"Tag_{tag.Key}", tag.Value));
            }
        }
    }

    private void AddEvents(LogEvent logEvent, ILogEventPropertyFactory propertyFactory, Activity activity)
    {
        var events = activity.Events.Select(e => new
        {
            e.Name,
            Timestamp = e.Timestamp.ToString("o"),
            Tags = e.Tags.ToDictionary(t => t.Key, t => t.Value)
        }).ToList();

        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("ActivityEvents", events, destructureObjects: true));
    }

    private void AddLinks(LogEvent logEvent, ILogEventPropertyFactory propertyFactory, Activity activity)
    {
        var links = activity.Links.Select(link => new
        {
            TraceId = link.Context.TraceId.ToString(),
            SpanId = link.Context.SpanId.ToString(),
            TraceState = link.Context.TraceState,
            Tags = link.Tags.ToDictionary(t => t.Key, t => t.Value)
        }).ToList();

        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("ActivityLinks", links, destructureObjects: true));
    }
}

/// <summary>
/// Extension methods for Serilog configuration
/// </summary>
public static class SpanLoggerConfigurationExtensions
{
    public static LoggerConfiguration WithSpan(
        this LoggerEnrichmentConfiguration enrichmentConfiguration,
        SpanOptions? options = null)
    {
        return enrichmentConfiguration.With(new SpanEnricher(options ?? new SpanOptions()));
    }
}
