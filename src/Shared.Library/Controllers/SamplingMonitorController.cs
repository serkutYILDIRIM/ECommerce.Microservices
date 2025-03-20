using Microsoft.AspNetCore.Mvc;
using OpenTelemetry.Trace;
using Shared.Library.Telemetry.Sampling;

namespace Shared.Library.Controllers;

/// <summary>
/// Controller for monitoring and adjusting sampling configuration at runtime
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class SamplingMonitorController : ControllerBase
{
    private readonly ILogger<SamplingMonitorController> _logger;
    private readonly SamplingConfiguration _samplingConfig;

    /// <summary>
    /// Creates a new instance of the sampling monitor controller
    /// </summary>
    public SamplingMonitorController(
        ILogger<SamplingMonitorController> logger,
        SamplingConfiguration samplingConfig)
    {
        _logger = logger;
        _samplingConfig = samplingConfig;
    }

    /// <summary>
    /// Gets the current sampling configuration
    /// </summary>
    [HttpGet]
    public IActionResult GetSamplingConfig()
    {
        return Ok(new
        {
            SamplerType = _samplingConfig.SamplerType.ToString(),
            SamplingProbability = _samplingConfig.SamplingProbability,
            MaxTracesPerSecond = _samplingConfig.MaxTracesPerSecond,
            UseCompositeSampling = _samplingConfig.UseCompositeSampling,
            RulesCount = _samplingConfig.Rules.Count,
            Rules = _samplingConfig.Rules.Select(r => new
            {
                r.Name,
                r.SpanNamePatterns,
                r.SpanKinds,
                AttributeMatches = r.AttributeMatches.Select(a => new 
                {
                    a.Key,
                    a.Value,
                    a.ExactMatch
                }).ToList(),
                SamplingDecision = r.SamplingDecision.ToString()
            }).ToList()
        });
    }

    /// <summary>
    /// Updates the sampling probability
    /// </summary>
    [HttpPost("probability")]
    public IActionResult UpdateSamplingProbability([FromQuery] double probability)
    {
        if (probability < 0.0 || probability > 1.0)
        {
            return BadRequest("Probability must be between 0.0 and 1.0");
        }

        var oldValue = _samplingConfig.SamplingProbability;
        _samplingConfig.SamplingProbability = probability;
        
        _logger.LogInformation("Updated sampling probability from {OldValue} to {NewValue}", 
            oldValue, probability);
        
        return Ok(new { Message = $"Updated sampling probability to {probability}" });
    }

    /// <summary>
    /// Updates the maximum traces per second
    /// </summary>
    [HttpPost("rate-limit")]
    public IActionResult UpdateRateLimit([FromQuery] int maxTracesPerSecond)
    {
        if (maxTracesPerSecond < 1)
        {
            return BadRequest("Maximum traces per second must be at least 1");
        }

        var oldValue = _samplingConfig.MaxTracesPerSecond;
        _samplingConfig.MaxTracesPerSecond = maxTracesPerSecond;
        
        _logger.LogInformation("Updated maximum traces per second from {OldValue} to {NewValue}", 
            oldValue, maxTracesPerSecond);
        
        return Ok(new { Message = $"Updated maximum traces per second to {maxTracesPerSecond}" });
    }

    /// <summary>
    /// Adds a new sampling rule
    /// </summary>
    [HttpPost("rules")]
    public IActionResult AddSamplingRule([FromBody] SamplingRule rule)
    {
        if (string.IsNullOrEmpty(rule.Name))
        {
            return BadRequest("Rule name is required");
        }

        if (_samplingConfig.Rules.Any(r => r.Name == rule.Name))
        {
            return BadRequest($"Rule with name '{rule.Name}' already exists");
        }

        _samplingConfig.Rules.Add(rule);
        
        _logger.LogInformation("Added new sampling rule: {RuleName}", rule.Name);
        
        return CreatedAtAction(nameof(GetSamplingConfig), null);
    }

    /// <summary>
    /// Deletes a sampling rule
    /// </summary>
    [HttpDelete("rules/{name}")]
    public IActionResult DeleteSamplingRule(string name)
    {
        var rule = _samplingConfig.Rules.FirstOrDefault(r => r.Name == name);
        if (rule == null)
        {
            return NotFound($"Rule with name '{name}' not found");
        }

        _samplingConfig.Rules.Remove(rule);
        
        _logger.LogInformation("Deleted sampling rule: {RuleName}", name);
        
        return NoContent();
    }
}
