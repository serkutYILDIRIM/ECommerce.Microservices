using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Trace;
using Shared.Library.Telemetry.Exporters;
using System.Diagnostics;

namespace Shared.Library.Controllers;

/// <summary>
/// Controller for testing and accessing telemetry data
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class TelemetryTestController : ControllerBase
{
    private readonly ILogger<TelemetryTestController> _logger;
    private readonly TelemetryStorage _telemetryStorage;
    private readonly string _serviceName;
    private readonly CustomExporterOptions _options;

    public TelemetryTestController(
        ILogger<TelemetryTestController> logger,
        TelemetryStorage telemetryStorage,
        string serviceName,
        CustomExporterOptions options)
    {
        _logger = logger;
        _telemetryStorage = telemetryStorage;
        _serviceName = serviceName;
        _options = options;
    }

    /// <summary>
    /// Get recent spans to verify data flow
    /// </summary>
    [HttpGet("spans")]
    public IActionResult GetRecentSpans([FromQuery] int count = 100, [FromQuery] string? serviceName = null)
    {
        var spans = _telemetryStorage.GetRecentSpans(count, serviceName);
        return Ok(spans);
    }

    /// <summary>
    /// Get error spans
    /// </summary>
    [HttpGet("errors")]
    public IActionResult GetErrorSpans([FromQuery] int count = 100, [FromQuery] string? serviceName = null)
    {
        var spans = _telemetryStorage.GetErrorSpans(count, serviceName);
        return Ok(spans);
    }

    /// <summary>
    /// Get slow spans
    /// </summary>
    [HttpGet("slow")]
    public IActionResult GetSlowSpans([FromQuery] int count = 100, [FromQuery] string? serviceName = null)
    {
        var spans = _telemetryStorage.GetSlowSpans(count, serviceName);
        return Ok(spans);
    }

    /// <summary>
    /// Get a specific trace
    /// </summary>
    [HttpGet("trace/{traceId}")]
    public IActionResult GetTrace(string traceId)
    {
        var spans = _telemetryStorage.GetTraceById(traceId);
        if (!spans.Any())
        {
            return NotFound();
        }
        return Ok(spans);
    }

    /// <summary>
    /// Get telemetry storage statistics
    /// </summary>
    [HttpGet("stats")]
    public IActionResult GetStats()
    {
        var stats = _telemetryStorage.GetStats();
        return Ok(stats);
    }

    /// <summary>
    /// Clear all stored telemetry data
    /// </summary>
    [HttpPost("clear")]
    public IActionResult ClearStorage()
    {
        _telemetryStorage.Clear();
        return Ok(new { message = "Telemetry storage cleared" });
    }

    /// <summary>
    /// Create a test span for verification
    /// </summary>
    [HttpPost("test")]
    public IActionResult CreateTestSpan([FromQuery] bool createError = false, [FromQuery] bool createSlow = false)
    {
        var source = new ActivitySource(_serviceName);
        
        using var activity = source.StartActivity("TestSpan");
        if (activity != null)
        {
            activity.SetTag("test.timestamp", DateTime.UtcNow);
            activity.SetTag("test.source", _serviceName);
            
            if (createSlow)
            {
                _logger.LogInformation("Creating slow test span");
                activity.SetTag("test.slow", true);
                
                // Simulate a slow operation
                Thread.Sleep((int)_options.SlowSpanThresholdMs + 100);
            }
            
            if (createError)
            {
                _logger.LogInformation("Creating error test span");
                activity.SetTag("test.error", true);
                activity.SetStatus(ActivityStatusCode.Error, "Test error");
                activity.RecordException(new Exception("Test exception"));
            }
        }
        
        return Ok(new 
        { 
            message = "Test span created", 
            traceId = activity?.TraceId.ToString(), 
            spanId = activity?.SpanId.ToString(),
            slow = createSlow,
            error = createError
        });
    }
}
