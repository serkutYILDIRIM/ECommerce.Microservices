using System.Diagnostics;

namespace Shared.Library.Telemetry.Contexts;

/// <summary>
/// Provides functionality for running background tasks while preserving Activity context
/// </summary>
public static class ContextAwareBackgroundTask
{
    /// <summary>
    /// Runs a task in the background while preserving the current Activity context
    /// </summary>
    public static Task Run(Func<Task> function, CancellationToken cancellationToken = default)
    {
        var currentActivity = Activity.Current;

        return Task.Run(async () =>
        {
            using var scope = new ActivityPropagationScope(currentActivity);
            await function();
        }, cancellationToken);
    }

    /// <summary>
    /// Runs a task in the background while preserving the current Activity context
    /// </summary>
    public static Task<TResult> Run<TResult>(Func<Task<TResult>> function, CancellationToken cancellationToken = default)
    {
        var currentActivity = Activity.Current;

        return Task.Run(async () =>
        {
            using var scope = new ActivityPropagationScope(currentActivity);
            return await function();
        }, cancellationToken);
    }

    /// <summary>
    /// Runs an action in the background while preserving the current Activity context
    /// </summary>
    public static Task Run(Action action, CancellationToken cancellationToken = default)
    {
        var currentActivity = Activity.Current;

        return Task.Run(() =>
        {
            using var scope = new ActivityPropagationScope(currentActivity);
            action();
        }, cancellationToken);
    }

    /// <summary>
    /// Runs a function in the background while preserving the current Activity context
    /// </summary>
    public static Task<TResult> Run<TResult>(Func<TResult> function, CancellationToken cancellationToken = default)
    {
        var currentActivity = Activity.Current;

        return Task.Run(() =>
        {
            using var scope = new ActivityPropagationScope(currentActivity);
            return function();
        }, cancellationToken);
    }
}
