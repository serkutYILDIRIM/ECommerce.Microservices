using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Shared.Library.Telemetry.Contexts;
using System.Diagnostics;

namespace Shared.Library.Controllers;

/// <summary>
/// Controller for testing and demonstrating async context propagation
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AsyncContextTestController : ControllerBase
{
    private readonly ILogger<AsyncContextTestController> _logger;

    public AsyncContextTestController(ILogger<AsyncContextTestController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Tests context propagation with Task.Run
    /// </summary>
    [HttpGet("taskrun")]
    public async Task<IActionResult> TestTaskRun()
    {
        var parentActivity = Activity.Current;
        if (parentActivity == null)
        {
            _logger.LogWarning("No parent activity found at test start");
            return Problem("No parent activity found at test start");
        }

        _logger.LogInformation("Parent TraceId: {TraceId}, SpanId: {SpanId}",
            parentActivity.TraceId, parentActivity.SpanId);

        // Test Task.Run with context propagation
        var result = await ContextAwareBackgroundTask.Run(() =>
        {
            var childActivity = Activity.Current;
            if (childActivity == null)
            {
                _logger.LogError("No activity found in background task");
                return Task.FromResult(new { Success = false, Message = "Context lost" });
            }

            _logger.LogInformation("Child TraceId: {TraceId}, SpanId: {SpanId}, ParentSpanId: {ParentSpanId}",
                childActivity.TraceId, childActivity.SpanId, childActivity.ParentSpanId);

            // Verify that the trace ID matches but span ID differs (new span in same trace)
            bool success = childActivity.TraceId.Equals(parentActivity.TraceId);
            string message = success ? "Context maintained" : "Trace ID mismatch";

            return Task.FromResult(new { Success = success, Message = message });
        });

        return Ok(new
        {
            ParentTraceId = parentActivity.TraceId.ToString(),
            ParentSpanId = parentActivity.SpanId.ToString(),
            result
        });
    }

    /// <summary>
    /// Tests context propagation with Task continuations
    /// </summary>
    [HttpGet("taskcontinuations")]
    public async Task<IActionResult> TestTaskContinuations()
    {
        var parentActivity = Activity.Current;
        if (parentActivity == null)
        {
            _logger.LogWarning("No parent activity found at test start");
            return Problem("No parent activity found at test start");
        }

        // Add baggage items to check if they propagate
        parentActivity.AddBaggage("test.key", "test.value");

        var results = new List<object>();

        // First task with a simple continuation
        var task1 = Task.Delay(10).RunWithContext(() =>
        {
            var activity = Activity.Current;
            return new
            {
                TraceId = activity?.TraceId.ToString() ?? "None",
                SpanId = activity?.SpanId.ToString() ?? "None",
                ParentSpanId = activity?.ParentSpanId.ToString() ?? "None",
                HasTestBaggage = activity?.GetBaggageItem("test.key") == "test.value"
            };
        });

        // Second task with an async continuation
        var task2 = Task.Delay(20).RunWithContext(async () =>
        {
            // Add a small delay to simulate async work
            await Task.Delay(5);
            var activity = Activity.Current;
            return new
            {
                TraceId = activity?.TraceId.ToString() ?? "None",
                SpanId = activity?.SpanId.ToString() ?? "None",
                ParentSpanId = activity?.ParentSpanId.ToString() ?? "None",
                HasTestBaggage = activity?.GetBaggageItem("test.key") == "test.value",
                ExecutedAsyncWork = true
            };
        });

        // Run multiple tasks in parallel to ensure context isolation
        var result1 = await task1;
        var result2 = await task2;

        return Ok(new
        {
            ParentTraceId = parentActivity.TraceId.ToString(),
            ParentSpanId = parentActivity.SpanId.ToString(),
            ContinuationTask = result1,
            AsyncContinuationTask = result2
        });
    }

    /// <summary>
    /// Tests context propagation with AsyncContext helper
    /// </summary>
    [HttpGet("asynccontext")]
    public async Task<IActionResult> TestAsyncContext()
    {
        var parentActivity = Activity.Current;
        if (parentActivity == null)
        {
            _logger.LogWarning("No parent activity found at test start");
            return Problem("No parent activity found at test start");
        }

        // Add baggage items to check if they propagate
        parentActivity.AddBaggage("context.key", "context.value");

        // Capture the current context
        var capturedContext = AsyncContext.Capture();

        // Simulate work in a background thread pool task
        var result = await Task.Run(async () =>
        {
            // First check context without applying our captured context
            var beforeActivity = Activity.Current;
            var beforeResult = new
            {
                HasActivity = beforeActivity != null,
                TraceId = beforeActivity?.TraceId.ToString() ?? "None",
                SpanId = beforeActivity?.SpanId.ToString() ?? "None",
                HasBaggage = beforeActivity?.GetBaggageItem("context.key") == "context.value"
            };

            // Now run with our captured context
            var afterResult = await AsyncContext.RunAsync(async () =>
            {
                // Add a delay to ensure we're testing true async behavior
                await Task.Delay(10);
                
                var afterActivity = Activity.Current;
                return new
                {
                    HasActivity = afterActivity != null,
                    TraceId = afterActivity?.TraceId.ToString() ?? "None",
                    SpanId = afterActivity?.SpanId.ToString() ?? "None",
                    HasBaggage = afterActivity?.GetBaggageItem("context.key") == "context.value"
                };
            }, capturedContext);

            return new { BeforeApplyingContext = beforeResult, AfterApplyingContext = afterResult };
        });

        return Ok(new
        {
            ParentTraceId = parentActivity.TraceId.ToString(),
            ParentSpanId = parentActivity.SpanId.ToString(),
            result
        });
    }

    /// <summary>
    /// Tests context propagation through multiple async boundaries
    /// </summary>
    [HttpGet("nestedasync")]
    public async Task<IActionResult> TestNestedAsync()
    {
        var parentActivity = Activity.Current;
        if (parentActivity == null)
        {
            _logger.LogWarning("No parent activity found at test start");
            return Problem("No parent activity found at test start");
        }

        // Add unique identifier to the activity
        parentActivity.SetTag("test.id", Guid.NewGuid().ToString());

        // Create a chain of nested async calls
        var result = await NestedAsyncLevel1();

        return Ok(new
        {
            ParentTraceId = parentActivity.TraceId.ToString(),
            ParentSpanId = parentActivity.SpanId.ToString(),
            NestedResults = result
        });
    }

    private async Task<List<object>> NestedAsyncLevel1()
    {
        var results = new List<object>();
        var activity = Activity.Current;

        results.Add(new
        {
            Level = 1,
            TraceId = activity?.TraceId.ToString() ?? "None",
            SpanId = activity?.SpanId.ToString() ?? "None",
            ParentSpanId = activity?.ParentSpanId.ToString() ?? "None",
            TestId = activity?.GetTagItem("test.id")?.ToString() ?? "None"
        });

        // First approach: use Task.Delay without special handling
        await Task.Delay(10);
        results.Add(GetActivityInfo("1 after Task.Delay"));

        // Second approach: use Task.Delay with WithActivityContext
        await Task.Delay(10).WithActivityContext();
        results.Add(GetActivityInfo("1 after Task.Delay with WithActivityContext"));

        // Call next level
        var level2Results = await NestedAsyncLevel2();
        results.AddRange(level2Results);

        return results;
    }

    private async Task<List<object>> NestedAsyncLevel2()
    {
        var results = new List<object>();
        var activity = Activity.Current;

        results.Add(new
        {
            Level = 2,
            TraceId = activity?.TraceId.ToString() ?? "None",
            SpanId = activity?.SpanId.ToString() ?? "None",
            ParentSpanId = activity?.ParentSpanId.ToString() ?? "None",
            TestId = activity?.GetTagItem("test.id")?.ToString() ?? "None"
        });

        // Use AsyncContext for deep nesting
        var capturedContext = AsyncContext.Capture();

        // Create multi-threaded work
        var tasks = new List<Task<object>>();
        
        // Add 3 parallel tasks
        for (int i = 0; i < 3; i++)
        {
            int taskId = i;
            tasks.Add(Task.Run(async () =>
            {
                return await AsyncContext.RunAsync(async () =>
                {
                    await Task.Delay(10 * taskId);
                    var taskActivity = Activity.Current;
                    return new
                    {
                        Level = $"2.{taskId}",
                        TraceId = taskActivity?.TraceId.ToString() ?? "None",
                        SpanId = taskActivity?.SpanId.ToString() ?? "None",
                        ParentSpanId = taskActivity?.ParentSpanId.ToString() ?? "None",
                        TestId = taskActivity?.GetTagItem("test.id")?.ToString() ?? "None"
                    };
                }, capturedContext);
            }));
        }

        var parallelResults = await Task.WhenAll(tasks);
        results.AddRange(parallelResults);

        return results;
    }

    private object GetActivityInfo(string label)
    {
        var activity = Activity.Current;
        return new
        {
            Label = label,
            TraceId = activity?.TraceId.ToString() ?? "None",
            SpanId = activity?.SpanId.ToString() ?? "None",
            ParentSpanId = activity?.ParentSpanId.ToString() ?? "None",
            TestId = activity?.GetTagItem("test.id")?.ToString() ?? "None"
        };
    }
}
