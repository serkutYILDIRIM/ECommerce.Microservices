using System.Diagnostics;

namespace Shared.Library.Telemetry.Contexts;

/// <summary>
/// Provides a scope for propagating the current Activity context across async boundaries
/// </summary>
public class ActivityPropagationScope : IDisposable
{
    private readonly Activity? _originalActivity;
    private readonly Activity? _capturedActivity;
    private readonly bool _restoreOnDispose;
    private bool _isDisposed;

    /// <summary>
    /// Creates a new scope that captures the current Activity
    /// </summary>
    /// <param name="restoreOnDispose">Whether to restore the original Activity when disposed</param>
    public ActivityPropagationScope(bool restoreOnDispose = true)
    {
        _originalActivity = Activity.Current;
        _capturedActivity = _originalActivity;
        _restoreOnDispose = restoreOnDispose;
    }

    /// <summary>
    /// Creates a new scope with a specific Activity
    /// </summary>
    /// <param name="activity">The Activity to set as current</param>
    /// <param name="restoreOnDispose">Whether to restore the original Activity when disposed</param>
    public ActivityPropagationScope(Activity? activity, bool restoreOnDispose = true)
    {
        _originalActivity = Activity.Current;
        _capturedActivity = activity;
        _restoreOnDispose = restoreOnDispose;
        
        // Set the captured activity as current immediately
        if (_capturedActivity != null)
        {
            Activity.Current = _capturedActivity;
        }
    }

    /// <summary>
    /// Restores the current Activity to the one that was active when this scope was created
    /// </summary>
    public void Dispose()
    {
        if (_isDisposed)
            return;

        if (_restoreOnDispose)
        {
            Activity.Current = _originalActivity;
        }

        _isDisposed = true;
    }

    /// <summary>
    /// Captures the current Activity
    /// </summary>
    public static ActivityPropagationScope Capture()
    {
        return new ActivityPropagationScope();
    }

    /// <summary>
    /// Restores a captured Activity to be the current one
    /// </summary>
    public void Restore()
    {
        if (_capturedActivity != null)
        {
            Activity.Current = _capturedActivity;
        }
    }
}
