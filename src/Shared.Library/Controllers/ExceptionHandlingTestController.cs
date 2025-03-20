using Microsoft.AspNetCore.Mvc;
using Shared.Library.Telemetry;
using System.Diagnostics;

namespace Shared.Library.Controllers;

/// <summary>
/// Controller for testing exception handling and tracking
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class ExceptionHandlingTestController : ControllerBase
{
    private readonly ILogger<ExceptionHandlingTestController> _logger;

    public ExceptionHandlingTestController(ILogger<ExceptionHandlingTestController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Tests unhandled exceptions caught by middleware
    /// </summary>
    [HttpGet("unhandled")]
    public IActionResult ThrowUnhandledException()
    {
        // This exception will be caught by the ExceptionHandlingMiddleware
        throw new InvalidOperationException("This is an unhandled exception test");
    }

    /// <summary>
    /// Tests manually handled exceptions with proper span recording
    /// </summary>
    [HttpGet("handled")]
    public IActionResult HandleExceptionWithSpan()
    {
        try
        {
            // Simulate a service call that fails
            SimulateServiceCall();
            return Ok(new { message = "This should not be reached" });
        }
        catch (Exception ex)
        {
            // Get current span
            var activity = Activity.Current;
            
            // Record the exception on the span with all details
            activity?.RecordException(ex);
            
            // Log with the trace ID for correlation
            _logger.LogError(ex, 
                "Handled exception in test controller. Trace ID: {TraceId}", 
                activity?.TraceId.ToString() ?? "unavailable");
                
            // Return error response
            return StatusCode(500, new 
            { 
                message = "A handled exception occurred", 
                correlationId = activity?.TraceId.ToString(),
                error = ex.Message
            });
        }
    }

    /// <summary>
    /// Tests async exception with context preservation
    /// </summary>
    [HttpGet("async")]
    public async Task<IActionResult> AsyncExceptionTest()
    {
        // Capture context info for verification
        var originalActivity = Activity.Current;
        var originalTraceId = originalActivity?.TraceId.ToString();
        
        try
        {
            // Simulate an async operation that fails
            await Task.Run(() => 
            {
                // Verify context was maintained
                var taskActivity = Activity.Current;
                var taskTraceId = taskActivity?.TraceId.ToString();
                
                // Check if trace ID is preserved
                bool contextPreserved = taskTraceId == originalTraceId;
                
                // Log context verification
                _logger.LogInformation(
                    "Context verification - Original TraceId: {OriginalTraceId}, Task TraceId: {TaskTraceId}, Preserved: {Preserved}",
                    originalTraceId, taskTraceId, contextPreserved);
                    
                // Now throw an exception
                throw new TimeoutException("Async operation timed out");
            });
            
            return Ok(new { message = "This should not be reached" });
        }
        catch (Exception ex)
        {
            // Verify the current span is still available
            var currentActivity = Activity.Current;
            
            // Record exception on span
            currentActivity?.RecordException(ex);
            
            // Verify context was maintained
            bool contextPreserved = currentActivity?.TraceId.ToString() == originalTraceId;
            
            return StatusCode(500, new 
            { 
                message = "An async exception occurred", 
                correlationId = currentActivity?.TraceId.ToString(),
                contextPreserved,
                error = ex.Message
            });
        }
    }

    /// <summary>
    /// Tests nested exceptions and proper recording
    /// </summary>
    [HttpGet("nested")]
    public IActionResult NestedExceptionTest()
    {
        try
        {
            // Using a span makes the test more realistic
            using var activity = new ActivitySource("ExceptionTest")
                .StartActivity("OuterOperation");
                
            try
            {
                // Inner operation that fails
                using var innerActivity = new ActivitySource("ExceptionTest")
                    .StartActivity("InnerOperation");
                
                throw new ArgumentException("Invalid inner argument");
            }
            catch (Exception innerEx)
            {
                // Record the inner exception on its span
                Activity.Current?.RecordException(innerEx);
                
                // Wrap and rethrow
                throw new InvalidOperationException("Outer operation failed", innerEx);
            }
        }
        catch (Exception ex)
        {
            // Record on the outer span
            Activity.Current?.RecordException(ex);
            
            // Log the full exception chain
            _logger.LogError(ex, "Nested exception test failed");
            
            return StatusCode(500, new 
            { 
                message = "A nested exception occurred", 
                correlationId = Activity.Current?.TraceId.ToString(),
                error = ex.Message,
                innerError = ex.InnerException?.Message
            });
        }
    }
    
    /// <summary>
    /// Helper method to simulate a service call that fails
    /// </summary>
    private void SimulateServiceCall()
    {
        using var activity = new ActivitySource("SimulatedService")
            .StartActivity("ServiceOperation");
            
        activity?.SetTag("operation.name", "test-operation");
        activity?.SetTag("operation.params", "test-params");
        
        // Simulate processing before the error
        Thread.Sleep(50);
        
        // Throw a test exception
        throw new ApplicationException("Simulated service call failed");
    }
}
