using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Shared.Library.Telemetry.Contexts;

/// <summary>
/// Provides extension methods for Task to ensure Activity context flows correctly across async boundaries
/// </summary>
public static class TaskExtensions
{
    /// <summary>
    /// Runs a function with the current Activity context preserved
    /// </summary>
    public static Task RunWithContext(this Task task, Action continuation)
    {
        var currentActivity = Activity.Current;
        return task.ContinueWith(_ =>
        {
            using var scope = new ActivityPropagationScope(currentActivity);
            continuation();
        }, TaskScheduler.Current);
    }

    /// <summary>
    /// Runs a function with the current Activity context preserved
    /// </summary>
    public static Task RunWithContext(this Task task, Func<Task> continuation)
    {
        var currentActivity = Activity.Current;
        return task.ContinueWith(async _ =>
        {
            using var scope = new ActivityPropagationScope(currentActivity);
            await continuation();
        }, TaskScheduler.Current).Unwrap();
    }

    /// <summary>
    /// Runs a function with the current Activity context preserved
    /// </summary>
    public static Task<TResult> RunWithContext<TResult>(this Task task, Func<TResult> continuation)
    {
        var currentActivity = Activity.Current;
        return task.ContinueWith(_ =>
        {
            using var scope = new ActivityPropagationScope(currentActivity);
            return continuation();
        }, TaskScheduler.Current);
    }

    /// <summary>
    /// Runs a function with the current Activity context preserved
    /// </summary>
    public static Task<TResult> RunWithContext<TResult>(this Task task, Func<Task<TResult>> continuation)
    {
        var currentActivity = Activity.Current;
        return task.ContinueWith(async _ =>
        {
            using var scope = new ActivityPropagationScope(currentActivity);
            return await continuation();
        }, TaskScheduler.Current).Unwrap();
    }

    /// <summary>
    /// Runs a function with the current Activity context preserved
    /// </summary>
    public static Task<TResult> RunWithContext<T, TResult>(this Task<T> task, Func<T, TResult> continuation)
    {
        var currentActivity = Activity.Current;
        return task.ContinueWith(t =>
        {
            using var scope = new ActivityPropagationScope(currentActivity);
            return continuation(t.Result);
        }, TaskScheduler.Current);
    }

    /// <summary>
    /// Runs a function with the current Activity context preserved
    /// </summary>
    public static Task<TResult> RunWithContext<T, TResult>(this Task<T> task, Func<T, Task<TResult>> continuation)
    {
        var currentActivity = Activity.Current;
        return task.ContinueWith(async t =>
        {
            using var scope = new ActivityPropagationScope(currentActivity);
            return await continuation(t.Result);
        }, TaskScheduler.Current).Unwrap();
    }

    /// <summary>
    /// Creates a ConfiguredTaskAwaitable that preserves the current Activity context
    /// </summary>
    public static ConfiguredTaskAwaitable WithActivityContext(this Task task)
    {
        var currentActivity = Activity.Current;
        return task.ConfigureAwait(false);
    }

    /// <summary>
    /// Creates a ConfiguredTaskAwaitable<T> that preserves the current Activity context
    /// </summary>
    public static ConfiguredTaskAwaitable<T> WithActivityContext<T>(this Task<T> task)
    {
        var currentActivity = Activity.Current;
        return task.ConfigureAwait(false);
    }
}
