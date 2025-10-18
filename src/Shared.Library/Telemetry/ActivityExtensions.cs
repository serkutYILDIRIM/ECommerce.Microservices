using System.Diagnostics;

namespace Shared.Library.Telemetry;

/// <summary>
/// Extension methods for working with Activity in OpenTelemetry
/// </summary>
public static class ActivityExtensions
{
    /// <summary>
    /// Records an exception on the current activity
    /// </summary>
    /// <param name="activity">The activity to record the exception on</param>
    /// <param name="exception">The exception to record</param>
    /// <param name="addErrorAttributes">Whether to also add error tags to the span</param>
    public static void RecordException(this Activity? activity, Exception exception, bool addErrorAttributes = true)
    {
        if (activity == null) return;

        // Create tags dictionary with exception details
        var tags = new ActivityTagsCollection
        {
            { "exception.type", exception.GetType().FullName },
            { "exception.message", exception.Message }
        };

        // Add stack trace if available
        if (!string.IsNullOrEmpty(exception.StackTrace))
        {
            tags.Add("exception.stacktrace", exception.StackTrace);
        }

        // Add inner exception details if available
        if (exception.InnerException != null)
        {
            tags.Add("exception.inner.type", exception.InnerException.GetType().FullName);
            tags.Add("exception.inner.message", exception.InnerException.Message);
        }

        // Record as an event on the current span
        activity.AddEvent(new ActivityEvent("exception", default, tags));

        // Set activity status to Error if requested
        if (addErrorAttributes)
        {
            activity.SetStatus(ActivityStatusCode.Error, exception.Message);
            activity.SetTag("error", true);
            activity.SetTag("error.type", exception.GetType().Name);
            activity.SetTag("error.message", exception.Message);
        }
    }

    /// <summary>
    /// Sets the current span status to error with details from the exception
    /// </summary>
    /// <param name="activity">The activity to set error status on</param>
    /// <param name="exception">The exception that occurred</param>
    /// <param name="recordExceptionEvent">Whether to also record an exception event</param>
    public static void SetErrorStatus(this Activity? activity, Exception exception, bool recordExceptionEvent = true)
    {
        if (activity == null) return;

        activity.SetStatus(ActivityStatusCode.Error, exception.Message);
        activity.SetTag("error", true);
        activity.SetTag("error.type", exception.GetType().Name);
        activity.SetTag("error.message", exception.Message);

        if (recordExceptionEvent)
        {
            activity.RecordException(exception, false);
        }
    }

    /// <summary>
    /// Adds standard error attributes to the current span
    /// </summary>
    /// <param name="activity">The activity to add attributes to</param>
    /// <param name="errorType">The type or category of error</param>
    /// <param name="message">The error message</param>
    public static void SetError(this Activity? activity, string errorType, string message)
    {
        if (activity == null) return;

        activity.SetStatus(ActivityStatusCode.Error, message);
        activity.SetTag("error", true);
        activity.SetTag("error.type", errorType);
        activity.SetTag("error.message", message);
    }
}
