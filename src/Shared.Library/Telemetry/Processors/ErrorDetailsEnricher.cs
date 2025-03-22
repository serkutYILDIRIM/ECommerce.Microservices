using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Shared.Library.Telemetry.Processors;

/// <summary>
/// Enriches spans with error details when exceptions occur
/// </summary>
public class ErrorDetailsEnricher : ISpanEnricher
{
    private readonly ILogger<ErrorDetailsEnricher> _logger;

    public ErrorDetailsEnricher(ILogger<ErrorDetailsEnricher> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Nothing to add at span start for error information
    /// </summary>
    public void EnrichSpanAtStart(Activity span)
    {
        // Error details only added at span end
    }

    /// <summary>
    /// Adds detailed error information to spans that contain exceptions
    /// </summary>
    public void EnrichSpanAtEnd(Activity span)
    {
        // Check if this span has error information
        if (span.Status != ActivityStatusCode.Error) return;

        try
        {
            // Check for recorded exception events
            var exceptionEvent = span.Events.FirstOrDefault(e => e.Name == "exception");
            if (!exceptionEvent.Equals(default(ActivityEvent)))
            {
                // Extract exception details from the event
                var exceptionTags = exceptionEvent.Tags.ToDictionary(t => t.Key, t => t.Value);

                if (exceptionTags.TryGetValue("exception.type", out var exceptionType))
                {
                    span.SetTag("error.type", exceptionType);
                }

                if (exceptionTags.TryGetValue("exception.message", out var exceptionMessage))
                {
                    span.SetTag("error.message", exceptionMessage);
                }

                if (exceptionTags.TryGetValue("exception.stacktrace", out var stackTrace))
                {
                    // Truncate stack trace to avoid excessive data
                    var truncatedStack = TruncateStackTrace(stackTrace?.ToString() ?? string.Empty);
                    span.SetTag("error.stack", truncatedStack);
                }
            }

            // Add error context flag
            span.SetTag("error", true);

            // Add category of error if identifiable
            if (span.Tags.Any(t => t.Key == "http.status_code"))
            {
                if (span.GetTagItem("http.status_code") is string statusCodeStr &&
                    int.TryParse(statusCodeStr, out var statusCode))
                {
                    span.SetTag("error.category", statusCode >= 500 ? "server" : "client");
                }
            }
            else if (span.Tags.Any(t => t.Key.StartsWith("db.")))
            {
                span.SetTag("error.category", "database");
            }
            else
            {
                span.SetTag("error.category", "application");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error enriching span with error details");
        }
    }

    /// <summary>
    /// Truncates a stack trace to avoid excessive telemetry data
    /// </summary>
    private string TruncateStackTrace(string stackTrace)
    {
        const int maxLength = 4000;

        if (string.IsNullOrEmpty(stackTrace) || stackTrace.Length <= maxLength)
            return stackTrace;

        // Get approximately first N frames
        var lines = stackTrace.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
        var framesToKeep = Math.Min(10, lines.Length);

        var truncatedStack = string.Join(Environment.NewLine, lines.Take(framesToKeep));
        return truncatedStack + Environment.NewLine + "... [truncated]";
    }
}
