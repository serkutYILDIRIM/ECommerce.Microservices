using Microsoft.Extensions.Configuration;
using OpenTelemetry.Trace;
using System.Diagnostics;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Shared.Library.Telemetry.Sampling;

/// <summary>
/// Custom sampling strategies for OpenTelemetry trace data.
/// These samplers control how much telemetry is collected based on various conditions.
/// </summary>
public static class CustomSamplers
{
    /// <summary>
    /// Adds custom samplers to the trace provider builder.
    /// This method configures intelligent sampling based on error conditions,
    /// latency thresholds, and business value.
    /// </summary>
    /// <param name="builder">The TracerProviderBuilder to add samplers to</param>
    /// <param name="configuration">Configuration containing sampling settings</param>
    /// <param name="serviceName">The name of the service for tracking</param>
    /// <returns>The updated builder for chaining</returns>
    public static TracerProviderBuilder AddCustomSamplers(
        this TracerProviderBuilder builder,
        IConfiguration configuration,
        string serviceName)
    {
        // Get sampling configuration from appsettings.json
        var samplingConfig = configuration.GetSection("OpenTelemetry:Sampling").Get<SamplingConfiguration>() 
            ?? new SamplingConfiguration();
        
        // Register the custom composite sampler
        builder.SetSampler(new CompositeCustomSampler(
            serviceName,
            samplingConfig.DefaultSamplingRate,
            samplingConfig.ErrorBasedSamplingRate,
            samplingConfig.SlowTransactionThresholdMs,
            samplingConfig.BusinessValueSamplingRate));
        
        return builder;
    }
}

/// <summary>
/// ParentBased sampler that respects the parent span's sampling decision
/// </summary>
public class ParentBasedRatioSampler : Sampler
{
    private readonly Sampler _rootSampler;
    private readonly Sampler _remoteParentSampled;
    private readonly Sampler _remoteParentNotSampled;
    private readonly Sampler _localParentSampled;
    private readonly Sampler _localParentNotSampled;

    /// <summary>
    /// Creates a parent-based sampler that uses different strategies based on parent context
    /// </summary>
    /// <param name="rootSamplingRatio">Probability (between 0.0 and 1.0) that a root span will be sampled</param>
    public ParentBasedRatioSampler(double rootSamplingRatio)
    {
        _rootSampler = new TraceIdRatioBasedSampler(rootSamplingRatio);
        _remoteParentSampled = new AlwaysOnSampler();
        _remoteParentNotSampled = new AlwaysOffSampler();
        _localParentSampled = new AlwaysOnSampler();
        _localParentNotSampled = new AlwaysOffSampler();
    }

    /// <summary>
    /// Makes sampling decisions based on parent context
    /// </summary>
    public override SamplingResult ShouldSample(in SamplingParameters samplingParameters)
    {
        var parentContext = samplingParameters.ParentContext;
        
        if (parentContext.TraceId == default)
        {
            // This is a root span, use the configured probability
            return _rootSampler.ShouldSample(samplingParameters);
        }
        
        // If parent is remote (from another service)
        if (parentContext.IsRemote)
        {
            return parentContext.TraceFlags.HasFlag(ActivityTraceFlags.Recorded)
                ? _remoteParentSampled.ShouldSample(samplingParameters)
                : _remoteParentNotSampled.ShouldSample(samplingParameters);
        }
        
        // If parent is local (same service)
        return parentContext.TraceFlags.HasFlag(ActivityTraceFlags.Recorded)
            ? _localParentSampled.ShouldSample(samplingParameters)
            : _localParentNotSampled.ShouldSample(samplingParameters);
    }

    /// <summary>
    /// Description of this sampler
    /// </summary>
    public new string Description => $"ParentBasedRatioSampler({_rootSampler.Description})";
}

/// <summary>
/// Rate limiting sampler that limits the number of traces per time window
/// </summary>
public class RateLimitingSampler : Sampler
{
    private readonly int _maxTracesPerSecond;
    private readonly TimeSpan _interval;
    private readonly ConcurrentQueue<DateTimeOffset> _sampledTraces = new();
    private long _droppedTraces = 0;
    private readonly object _lock = new();

    /// <summary>
    /// Creates a sampler that limits the number of traces per time window
    /// </summary>
    /// <param name="maxTracesPerSecond">Maximum number of traces to sample per second</param>
    public RateLimitingSampler(int maxTracesPerSecond)
    {
        _maxTracesPerSecond = Math.Max(1, maxTracesPerSecond);
        _interval = TimeSpan.FromSeconds(1);
    }

    /// <summary>
    /// Makes sampling decisions based on the current rate
    /// </summary>
    public override SamplingResult ShouldSample(in SamplingParameters samplingParameters)
    {
        // Clean up old entries first
        CleanupOldEntries();
        
        // Check if we should sample based on rate limit
        lock (_lock)
        {
            if (_sampledTraces.Count < _maxTracesPerSecond)
            {
                _sampledTraces.Enqueue(DateTimeOffset.UtcNow);
                return new SamplingResult(SamplingDecision.RecordAndSample);
            }

            // We're over the limit, drop this trace
            Interlocked.Increment(ref _droppedTraces);
            return new SamplingResult(SamplingDecision.Drop);
        }
    }

    private void CleanupOldEntries()
    {
        var cutoff = DateTimeOffset.UtcNow - _interval;
        
        lock (_lock)
        {
            // Remove entries that are older than the interval
            while (_sampledTraces.TryPeek(out var oldestTimestamp) && oldestTimestamp < cutoff)
            {
                _sampledTraces.TryDequeue(out _);
            }
        }
    }

    /// <summary>
    /// Description of this sampler
    /// </summary>
    public new string Description => $"RateLimitingSampler({_maxTracesPerSecond}/s)";
    
    /// <summary>
    /// Gets the number of dropped traces due to rate limiting
    /// </summary>
    public long GetDroppedTraces() => Interlocked.Read(ref _droppedTraces);
}

/// <summary>
/// Conditional sampler that applies rules to determine if a trace should be sampled
/// </summary>
public class ConditionalSampler : Sampler
{
    private readonly Sampler _baseSampler;
    private readonly List<SamplingRule> _rules;
    private readonly ILogger<ConditionalSampler> _logger;

    /// <summary>
    /// Creates a sampler that applies rules to determine if a trace should be sampled
    /// </summary>
    /// <param name="baseSampler">The sampler to use if no rules match</param>
    /// <param name="rules">List of sampling rules to apply</param>
    /// <param name="logger">Logger for diagnostic information</param>
    public ConditionalSampler(
        Sampler baseSampler, 
        List<SamplingRule> rules,
        ILogger<ConditionalSampler> logger)
    {
        _baseSampler = baseSampler;
        _rules = rules;
        _logger = logger;
    }

    /// <summary>
    /// Makes sampling decisions based on configured rules
    /// </summary>
    public override SamplingResult ShouldSample(in SamplingParameters samplingParameters)
    {
        // Get the trace and span names
        var traceId = samplingParameters.TraceId;
        var spanName = samplingParameters.Name;
        
        // Get tags/attributes
        var spanKind = samplingParameters.Kind;
        var attributes = samplingParameters.Tags;
        
        try
        {
            // Check each rule
            foreach (var rule in _rules)
            {
                // Match by span name
                if (rule.SpanNamePatterns.Count > 0 && 
                    !rule.SpanNamePatterns.Any(pattern => MatchesPattern(spanName, pattern)))
                {
                    continue;
                }
                
                // Match by attributes
                if (rule.AttributeMatches.Count > 0)
                {
                    bool allAttributesMatch = true;
                    
                    foreach (var attributeMatch in rule.AttributeMatches)
                    {
                        if (!MatchesAttribute(attributes, attributeMatch))
                        {
                            allAttributesMatch = false;
                            break;
                        }
                    }
                    
                    if (!allAttributesMatch)
                        continue;
                }
                
                // Match by span kind - convert SpanKind to ActivityKind for comparison
                if (rule.SpanKinds.Count > 0 && !rule.SpanKinds.Contains((ActivityKind)spanKind))
                {
                    continue;
                }
                
                // If we get here, the rule matches - apply the rule's sampling decision
                _logger.LogDebug("Sampling rule '{RuleName}' matched for span '{SpanName}'", 
                    rule.Name, spanName);
                
                return rule.SamplingDecision == SamplingDecision.RecordAndSample
                    ? new SamplingResult(SamplingDecision.RecordAndSample)
                    : new SamplingResult(SamplingDecision.Drop);
            }
            
            // If no rules matched, use the base sampler
            return _baseSampler.ShouldSample(samplingParameters);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying conditional sampling rules for span '{SpanName}'", spanName);
            return _baseSampler.ShouldSample(samplingParameters);
        }
    }

    private bool MatchesPattern(string input, string pattern)
    {
        if (pattern.StartsWith("*") && pattern.EndsWith("*"))
        {
            var innerPattern = pattern.Trim('*');
            return input.Contains(innerPattern, StringComparison.OrdinalIgnoreCase);
        }
        else if (pattern.StartsWith("*"))
        {
            var suffix = pattern.TrimStart('*');
            return input.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
        }
        else if (pattern.EndsWith("*"))
        {
            var prefix = pattern.TrimEnd('*');
            return input.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            return string.Equals(input, pattern, StringComparison.OrdinalIgnoreCase);
        }
    }

    private bool MatchesAttribute(IEnumerable<KeyValuePair<string, object>> attributes, AttributeMatch attributeMatch)
    {
        foreach (var attribute in attributes)
        {
            if (attribute.Key == attributeMatch.Key)
            {
                string attributeValue = attribute.Value?.ToString() ?? string.Empty;
                
                // Check if the value matches the expected value
                if (attributeMatch.ExactMatch && attributeMatch.Value == attributeValue)
                {
                    return true;
                }
                // Check if the value contains the expected value
                else if (!attributeMatch.ExactMatch && 
                         attributeValue.Contains(attributeMatch.Value, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        
        return false;
    }

    /// <summary>
    /// Description of this sampler
    /// </summary>
    public new string Description => $"ConditionalSampler({_rules.Count} rules, {_baseSampler.Description})";
}

/// <summary>
/// Combines multiple samplers with logical AND
/// </summary>
public class CompositeSampler : Sampler
{
    private readonly List<Sampler> _samplers;

    /// <summary>
    /// Creates a sampler that combines multiple samplers with logical AND
    /// </summary>
    /// <param name="samplers">List of samplers to combine</param>
    public CompositeSampler(List<Sampler> samplers)
    {
        _samplers = samplers;
    }

    /// <summary>
    /// Makes sampling decisions by combining all samplers
    /// </summary>
    public override SamplingResult ShouldSample(in SamplingParameters samplingParameters)
    {
        // If any sampler decides to drop, we drop
        foreach (var sampler in _samplers)
        {
            var result = sampler.ShouldSample(samplingParameters);
            if (result.Decision == SamplingDecision.Drop)
            {
                return result;
            }
        }
        
        // All samplers agreed to sample
        return new SamplingResult(SamplingDecision.RecordAndSample);
    }

    /// <summary>
    /// Description of this sampler
    /// </summary>
    public new string Description => $"CompositeSampler({string.Join(" AND ", _samplers.Select(s => s.Description))})";
}

/// <summary>
/// A composite sampler that combines multiple sampling strategies.
/// This allows applying different sampling rates based on request characteristics.
/// </summary>
public class CompositeCustomSampler : Sampler
{
    private readonly string _serviceName;
    private readonly double _baseRate;
    private readonly double _errorSamplingRate;
    private readonly double _latencyThresholdMs;
    private readonly double _highValueSamplingRate;
    
    // Basic probability sampler for standard requests
    private readonly TraceIdRatioBasedSampler _baseSampler;
    
    // Sampler for high-value business transactions
    private readonly TraceIdRatioBasedSampler _highValueSampler;
    
    // Always sample errors at a higher rate
    private readonly TraceIdRatioBasedSampler _errorSampler;

    /// <summary>
    /// Creates a new composite sampler with multiple sampling strategies.
    /// </summary>
    /// <param name="serviceName">Service name for tracking sampling metrics</param>
    /// <param name="baseRate">Base sampling rate for normal traffic (0.0-1.0)</param>
    /// <param name="errorSamplingRate">Sampling rate for error transactions (0.0-1.0)</param>
    /// <param name="latencyThresholdMs">Threshold above which to consider a transaction "slow"</param>
    /// <param name="highValueSamplingRate">Sampling rate for high-value transactions (0.0-1.0)</param>
    public CompositeCustomSampler(
        string serviceName,
        double baseRate = 0.1,
        double errorSamplingRate = 1.0,
        double latencyThresholdMs = 500,
        double highValueSamplingRate = 0.5)
    {
        _serviceName = serviceName;
        _baseRate = baseRate;
        _errorSamplingRate = errorSamplingRate;
        _latencyThresholdMs = latencyThresholdMs;
        _highValueSamplingRate = highValueSamplingRate;
        
        // Initialize the individual samplers
        _baseSampler = new TraceIdRatioBasedSampler(baseRate);
        _highValueSampler = new TraceIdRatioBasedSampler(highValueSamplingRate);
        _errorSampler = new TraceIdRatioBasedSampler(errorSamplingRate);
    }

    /// <summary>
    /// Makes sampling decisions based on trace ID, parent context, and span kind.
    /// Implements intelligent sampling based on multiple criteria.
    /// </summary>
    /// <param name="samplingParameters">Parameters for the sampling decision</param>
    /// <returns>Sampling decision with result and attributes</returns>
    public override SamplingResult ShouldSample(in SamplingParameters samplingParameters)
    {
        // Always respect the parent decision to maintain trace integrity
        // If parent was sampled, we should also sample this span
        if (samplingParameters.ParentContext.TraceFlags.HasFlag(ActivityTraceFlags.Recorded))
        {
            return new SamplingResult(SamplingDecision.RecordAndSample);
        }
        
        // Get the span name and kind
        string spanName = samplingParameters.Name;
        ActivityKind spanKind = (ActivityKind)samplingParameters.Kind;
        
        // Add sampling rule attributes to explain the decision
        var attributes = new Dictionary<string, object>
        {
            ["sampling.rule"] = "default"
        };
        
        // Error-based sampling: if the span indicates an error, use high sampling rate
        if (samplingParameters.Tags != null)
        {
            foreach (var tag in samplingParameters.Tags)
            {
                // Check for error tags or status codes
                if ((tag.Key == "error" && tag.Value?.ToString() == "true") ||
                    (tag.Key == "http.status_code" && tag.Value?.ToString() is string statusCode && statusCode.StartsWith("5")))
                {
                    attributes["sampling.rule"] = "error";
                    // Use the error sampler instead of always recording
                    return new SamplingResult(_errorSampler.ShouldSample(samplingParameters).Decision, attributes);
                }
                
                // Check for latency indications
                if (tag.Key == "duration_ms" && tag.Value is double durationMs && durationMs > _latencyThresholdMs)
                {
                    attributes["sampling.rule"] = "latency";
                    return new SamplingResult(SamplingDecision.RecordAndSample, attributes);
                }
                
                // Check for high-value business transactions
                if (tag.Key == "business.value" && tag.Value?.ToString() == "high")
                {
                    attributes["sampling.rule"] = "high_value";
                    return _highValueSampler.ShouldSample(samplingParameters);
                }
            }
        }
        
        // Apply high-value sampling for specific operations
        if (IsHighValueOperation(spanName))
        {
            attributes["sampling.rule"] = "high_value_operation";
            return _highValueSampler.ShouldSample(samplingParameters);
        }
        
        // Apply normal base rate sampling for standard requests
        return _baseSampler.ShouldSample(samplingParameters);
    }

    /// <summary>
    /// Determines if an operation is high-value based on its name.
    /// High-value operations get sampled at a higher rate.
    /// </summary>
    /// <param name="spanName">The name of the span representing the operation</param>
    /// <returns>True if the operation is considered high-value</returns>
    private bool IsHighValueOperation(string spanName)
    {
        // Identify operations that represent important business transactions
        return spanName.Contains("Purchase", StringComparison.OrdinalIgnoreCase) ||
               spanName.Contains("Payment", StringComparison.OrdinalIgnoreCase) ||
               spanName.Contains("Checkout", StringComparison.OrdinalIgnoreCase) ||
               spanName.Contains("Login", StringComparison.OrdinalIgnoreCase) ||
               spanName.Contains("Registration", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets the description of this sampler for logging and debugging.
    /// </summary>
    /// <returns>A description of the sampler</returns>
    public new string Description => 
        $"CompositeCustomSampler(baseRate={_baseRate}, " +
        $"errorRate={_errorSamplingRate}, " +
        $"highValueRate={_highValueSamplingRate}, " +
        $"latencyThreshold={_latencyThresholdMs}ms)";
}

/// <summary>
/// Configuration for OpenTelemetry sampling settings
/// </summary>
public class SamplingConfiguration
{
    /// <summary>
    /// Base sampling rate for normal traffic (0.0-1.0)
    /// </summary>
    public double DefaultSamplingRate { get; set; } = 0.1;
    
    /// <summary>
    /// Sampling rate for error transactions (0.0-1.0)
    /// </summary>
    public double ErrorBasedSamplingRate { get; set; } = 1.0;
    
    /// <summary>
    /// Threshold in milliseconds above which to consider a transaction "slow"
    /// </summary>
    public double SlowTransactionThresholdMs { get; set; } = 500;
    
    /// <summary>
    /// Sampling rate for high-value transactions (0.0-1.0)
    /// </summary>
    public double BusinessValueSamplingRate { get; set; } = 0.5;
}

/// <summary>
/// Represents a rule for conditional sampling
/// </summary>
public class SamplingRule
{
    /// <summary>
    /// Name of the sampling rule
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Patterns to match span names against
    /// </summary>
    public List<string> SpanNamePatterns { get; set; } = new List<string>();
    
    /// <summary>
    /// Types of span kinds to match
    /// </summary>
    public List<ActivityKind> SpanKinds { get; set; } = new List<ActivityKind>();
    
    /// <summary>
    /// Attributes to match on spans
    /// </summary>
    public List<AttributeMatch> AttributeMatches { get; set; } = new List<AttributeMatch>();
    
    /// <summary>
    /// The sampling decision to apply when this rule matches
    /// </summary>
    public SamplingDecision SamplingDecision { get; set; } = SamplingDecision.RecordAndSample;
}

/// <summary>
/// Represents an attribute match condition for sampling rules
/// </summary>
public class AttributeMatch
{
    /// <summary>
    /// The attribute key to match
    /// </summary>
    public string Key { get; set; } = string.Empty;
    
    /// <summary>
    /// The attribute value to match
    /// </summary>
    public string Value { get; set; } = string.Empty;
    
    /// <summary>
    /// Whether to match exactly or perform a contains match
    /// </summary>
    public bool ExactMatch { get; set; } = true;
}
