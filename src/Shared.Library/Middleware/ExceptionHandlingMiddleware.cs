using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shared.Library.Models;
using Shared.Library.Telemetry.Baggage;
using System.Diagnostics;
using System.Net;
using System.Text.Json;

namespace Shared.Library.Middleware;

/// <summary>
/// Middleware for global exception handling with OpenTelemetry integration
/// </summary>
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly IHostEnvironment _environment;
    private readonly BaggageManager? _baggageManager;

    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> logger,
        IHostEnvironment environment,
        BaggageManager? baggageManager = null)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
        _baggageManager = baggageManager;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        // Capture the current activity for telemetry
        var activity = Activity.Current;

        // Get correlation ID (either from baggage or generate new one)
        string correlationId = _baggageManager?.GetCorrelationId() ??
                               context.TraceIdentifier ??
                               Guid.NewGuid().ToString();

        // Set the activity status to error
        if (activity != null)
        {
            // Mark the span as error
            activity.SetStatus(ActivityStatusCode.Error, exception.Message);

            // Add error tags for easier querying
            activity.SetTag("error", true);
            activity.SetTag("error.type", exception.GetType().Name);
            activity.SetTag("error.message", exception.Message);

            // Record the exception as a span event (shows up nicely in trace visualization)
            RecordExceptionEvent(activity, exception);
        }

        // Determine status code based on exception type
        var statusCode = DetermineStatusCode(exception);
        context.Response.StatusCode = (int)statusCode;
        context.Response.ContentType = "application/json";

        // Create the error response with appropriate detail level based on environment
        var errorResponse = CreateErrorResponse(exception, correlationId, statusCode);

        // Log the error with all the relevant context
        LogException(exception, context, correlationId, statusCode);

        // Serialize and write the error response
        await context.Response.WriteAsync(
            JsonSerializer.Serialize(errorResponse, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            }));
    }

    private void RecordExceptionEvent(Activity activity, Exception exception)
    {
        // Create tags dictionary with exception details
        var tags = new ActivityTagsCollection
        {
            { "exception.type", exception.GetType().FullName },
            { "exception.message", exception.Message },
            { "exception.stacktrace", exception.StackTrace }
        };

        // Add inner exception details if available
        if (exception.InnerException != null)
        {
            tags.Add("exception.inner.type", exception.InnerException.GetType().FullName);
            tags.Add("exception.inner.message", exception.InnerException.Message);
        }

        // Record as an event on the current span
        activity.AddEvent(new ActivityEvent("exception", default, tags));
    }

    private HttpStatusCode DetermineStatusCode(Exception exception)
    {
        // Map common exceptions to appropriate status codes
        return exception switch
        {
            ArgumentException => HttpStatusCode.BadRequest,
            KeyNotFoundException => HttpStatusCode.NotFound,
            UnauthorizedAccessException => HttpStatusCode.Unauthorized,
            InvalidOperationException => HttpStatusCode.BadRequest,
            NotImplementedException => HttpStatusCode.NotImplemented,
            TimeoutException => HttpStatusCode.GatewayTimeout,
            // Add more mappings as needed
            _ => HttpStatusCode.InternalServerError
        };
    }

    private ErrorResponse CreateErrorResponse(Exception exception, string correlationId, HttpStatusCode statusCode)
    {
        var errorResponse = new ErrorResponse
        {
            CorrelationId = correlationId,
            Status = (int)statusCode,
            Error = GetErrorTitle(statusCode),
            Message = exception.Message,
            Timestamp = DateTimeOffset.UtcNow
        };

        // In development, include more details
        if (_environment.IsDevelopment())
        {
            errorResponse.Details = exception.StackTrace;

            // Add inner exceptions if they exist
            if (exception.InnerException != null)
            {
                errorResponse.InnerError = new ErrorDetail
                {
                    Message = exception.InnerException.Message,
                    Type = exception.InnerException.GetType().Name,
                    StackTrace = exception.InnerException.StackTrace
                };
            }
        }

        return errorResponse;
    }

    private void LogException(Exception exception, HttpContext context, string correlationId, HttpStatusCode statusCode)
    {
        // Create a structured log with context
        _logger.LogError(exception,
            "Request failed with status {StatusCode}. TraceId: {TraceId}, CorrelationId: {CorrelationId}, Path: {Path}, Method: {Method}",
            (int)statusCode,
            Activity.Current?.TraceId.ToString() ?? "unavailable",
            correlationId,
            context.Request.Path,
            context.Request.Method);
    }

    private string GetErrorTitle(HttpStatusCode statusCode)
    {
        return statusCode switch
        {
            HttpStatusCode.BadRequest => "Bad Request",
            HttpStatusCode.Unauthorized => "Unauthorized",
            HttpStatusCode.Forbidden => "Forbidden",
            HttpStatusCode.NotFound => "Not Found",
            HttpStatusCode.RequestTimeout => "Request Timeout",
            HttpStatusCode.Conflict => "Conflict",
            HttpStatusCode.Gone => "Gone",
            HttpStatusCode.UnprocessableEntity => "Unprocessable Entity",
            HttpStatusCode.TooManyRequests => "Too Many Requests",
            HttpStatusCode.InternalServerError => "Internal Server Error",
            HttpStatusCode.NotImplemented => "Not Implemented",
            HttpStatusCode.BadGateway => "Bad Gateway",
            HttpStatusCode.ServiceUnavailable => "Service Unavailable",
            HttpStatusCode.GatewayTimeout => "Gateway Timeout",
            _ => "Error"
        };
    }
}

/// <summary>
/// Extension methods for the exception handling middleware
/// </summary>
public static class ExceptionHandlingMiddlewareExtensions
{
    public static IApplicationBuilder UseGlobalExceptionHandler(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<ExceptionHandlingMiddleware>();
    }
}
