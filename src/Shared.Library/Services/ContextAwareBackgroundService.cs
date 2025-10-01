using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shared.Library.Telemetry.Contexts;
using System.Diagnostics;

namespace Shared.Library.Services;

/// <summary>
/// Base class for background services that need to maintain trace context
/// </summary>
public abstract class ContextAwareBackgroundService : BackgroundService
{
    private readonly ILogger _logger;

    protected ContextAwareBackgroundService(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Executes the background service with proper trace context
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("{ServiceName} is starting", GetType().Name);

        try
        {
            // Create a root activity for the background service
            using var activity = new ActivitySource(GetType().Name)
                .StartActivity($"{GetType().Name}.Execute");

            if (activity != null)
            {
                activity.SetTag("service.component", "background_service");
                activity.SetTag("service.name", GetType().Name);

                // Add baggage items that may be useful for context propagation
                activity.AddBaggage("service.component", "background_service");
                activity.AddBaggage("service.name", GetType().Name);
            }

            // Execute with the root activity as context
            await ExecuteWithActivityAsync(stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Error executing {ServiceName}", GetType().Name);
            throw;
        }

        _logger.LogInformation("{ServiceName} is stopping", GetType().Name);
    }

    /// <summary>
    /// Override this method to implement the background service logic with trace context
    /// </summary>
    protected abstract Task ExecuteWithActivityAsync(CancellationToken stoppingToken);

    /// <summary>
    /// Creates a new activity for a specific operation within the background service
    /// </summary>
    protected Activity? StartOperation(string operationName)
    {
        var activity = new ActivitySource(GetType().Name)
            .StartActivity(operationName);

        if (activity != null)
        {
            activity.SetTag("service.component", "background_service");
            activity.SetTag("service.operation", operationName);
        }

        return activity;
    }

    /// <summary>
    /// Runs a task ensuring the current activity context is preserved
    /// </summary>
    protected Task RunWithActivityContext(Func<Task> action)
    {
        return ContextAwareBackgroundTask.Run(action);
    }

    /// <summary>
    /// Runs a task ensuring the current activity context is preserved
    /// </summary>
    protected Task<TResult> RunWithActivityContext<TResult>(Func<Task<TResult>> function)
    {
        return ContextAwareBackgroundTask.Run(function);
    }
}
