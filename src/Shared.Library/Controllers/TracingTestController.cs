using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Trace;
using System.Diagnostics;

namespace Shared.Library.Controllers;

[ApiController]
[Route("trace-test")]
public class TracingTestController : ControllerBase
{
    private readonly ILogger<TracingTestController> _logger;
    private readonly string _serviceName;

    public TracingTestController(ILogger<TracingTestController> logger, string serviceName)
    {
        _logger = logger;
        _serviceName = serviceName;
    }

    [HttpGet]
    public IActionResult GetTraceInfo()
    {
        var activity = Activity.Current;
        if (activity == null)
        {
            return BadRequest("No active trace context found");
        }

        var responseData = new Dictionary<string, object>
        {
            ["service"] = _serviceName,
            ["traceId"] = activity.TraceId.ToString(),
            ["spanId"] = activity.SpanId.ToString(),
            ["parentSpanId"] = activity.ParentSpanId.ToString(),
            ["correlationId"] = HttpContext.Request.Headers["x-correlation-id"].ToString(),
            ["timestamp"] = DateTime.UtcNow
        };

        // Add baggage items
        var baggage = new Dictionary<string, string>();
        foreach (var item in activity.Baggage)
        {
            baggage[item.Key] = item.Value;
        }
        responseData["baggage"] = baggage;

        // Add request headers for debugging
        var headers = new Dictionary<string, string>();
        foreach (var header in HttpContext.Request.Headers)
        {
            headers[header.Key] = header.Value.ToString();
        }
        responseData["requestHeaders"] = headers;

        _logger.LogInformation("Trace test executed on {ServiceName} with TraceId: {TraceId}", 
            _serviceName, activity.TraceId.ToString());

        return Ok(responseData);
    }

    [HttpGet("chain")]
    public async Task<IActionResult> TestTraceChain([FromServices] IHttpClientFactory clientFactory, string targetUrl)
    {
        if (string.IsNullOrEmpty(targetUrl))
        {
            return BadRequest("Target URL must be provided");
        }

        var activity = Activity.Current;
        if (activity == null)
        {
            return BadRequest("No active trace context found");
        }

        activity?.AddBaggage("test.chain.initiator", _serviceName);
        activity?.SetTag("test.chain.initiator", _serviceName);

        try
        {
            // Forward the request to the target service
            var client = clientFactory.CreateClient();
            var request = new HttpRequestMessage(HttpMethod.Get, targetUrl);
            
            // The TracingMessageHandler will handle context propagation
            var response = await client.SendAsync(request);
            
            if (!response.IsSuccessStatusCode)
            {
                return StatusCode((int)response.StatusCode, 
                    $"Target service returned status code: {response.StatusCode}");
            }

            var content = await response.Content.ReadAsStringAsync();
            
            var responseData = new Dictionary<string, object>
            {
                ["initiatingService"] = _serviceName,
                ["traceId"] = activity.TraceId.ToString(),
                ["targetResponse"] = content,
                ["timestamp"] = DateTime.UtcNow
            };

            _logger.LogInformation("Trace chain test executed from {ServiceName} to {TargetUrl} with TraceId: {TraceId}",
                _serviceName, targetUrl, activity.TraceId.ToString());

            return Ok(responseData);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.RecordException(ex);
            _logger.LogError(ex, "Error executing trace chain test to {TargetUrl}", targetUrl);
            return StatusCode(500, $"Error: {ex.Message}");
        }
    }
}
