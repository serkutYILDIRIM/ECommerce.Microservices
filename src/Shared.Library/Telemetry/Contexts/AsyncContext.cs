using System.Diagnostics;

namespace Shared.Library.Telemetry.Contexts;

/// <summary>
/// Provides utilities for managing context flow across asynchronous boundaries
/// </summary>
public static class AsyncContext
{
    /// <summary>
    /// Captures the current execution context including Activity context
    /// </summary>
    public static AsyncContextScope Capture()
    {
        return new AsyncContextScope();
    }

    /// <summary>
    /// Runs a function with the captured context
    /// </summary>
    public static TResult Run<TResult>(Func<TResult> function, AsyncContextScope context)
    {
        using (context.Apply())
        {
            return function();
        }
    }

    /// <summary>
    /// Runs an action with the captured context
    /// </summary>
    public static void Run(Action action, AsyncContextScope context)
    {
        using (context.Apply())
        {
            action();
        }
    }

    /// <summary>
    /// Runs an async function with the captured context
    /// </summary>
    public static async Task<TResult> RunAsync<TResult>(Func<Task<TResult>> function, AsyncContextScope context)
    {
        using (context.Apply())
        {
            return await function();
        }
    }

    /// <summary>
    /// Runs an async action with the captured context
    /// </summary>
    public static async Task RunAsync(Func<Task> action, AsyncContextScope context)
    {
        using (context.Apply())
        {
            await action();
        }
    }
}

/// <summary>
/// Represents a captured context that can be applied in another async operation
/// </summary>
public class AsyncContextScope
{
    private readonly Activity? _activity;
    private readonly Dictionary<string, string> _baggageItems = new();

    /// <summary>
    /// Creates a new context scope that captures the current Activity and other contexts
    /// </summary>
    internal AsyncContextScope()
    {
        _activity = Activity.Current;

        // Capture all baggage items
        if (_activity != null)
        {
            foreach (var item in _activity.Baggage)
            {
                _baggageItems[item.Key] = item.Value ?? string.Empty;
            }
        }
    }

    /// <summary>
    /// Applies the captured context to the current execution context
    /// </summary>
    public IDisposable Apply()
    {
        // Return a composite disposable that will restore the original context
        var originalActivity = Activity.Current;

        // Set the captured activity as current
        if (_activity != null)
        {
            Activity.Current = _activity;
        }

        return new ContextRestorer(originalActivity);
    }

    private class ContextRestorer : IDisposable
    {
        private readonly Activity? _originalActivity;
        private bool _disposed;

        public ContextRestorer(Activity? originalActivity)
        {
            _originalActivity = originalActivity;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                // Restore the original context
                Activity.Current = _originalActivity;
                _disposed = true;
            }
        }
    }
}
