using OpenTelemetry.Trace;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace Shared.Library.Telemetry.Sampling;

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
        
        if (!parentContext.IsValid)
        {
            // This is a root span, use the configured probability
            return _rootSampler.ShouldSample(samplingParameters);
        }
        
        // If parent is remote (from another service)
        if (parentContext.IsRemote)
        {
            return parentContext.IsSampled
                ? _remoteParentSampled.ShouldSample(samplingParameters)
                : _remoteParentNotSampled.ShouldSample(samplingParameters);
        }
        
        // If parent is local (same service)
        return parentContext.IsSampled
            ? _localParentSampled.ShouldSample(samplingParameters)
            : _localParentNotSampled.ShouldSample(samplingParameters);
    }

    /// <summary>
    /// Description of this sampler
    /// </summary>
    public override string Description => $"ParentBasedRatioSampler({_rootSampler.Description})";
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
    public override string Description => $"RateLimitingSampler({_maxTracesPerSecond}/s)";
    
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
        var attributes = samplingParameters.Attributes;
        
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
                
                // Match by span kind
                if (rule.SpanKinds.Count > 0 && !rule.SpanKinds.Contains(spanKind))
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
    public override string Description => $"ConditionalSampler({_rules.Count} rules, {_baseSampler.Description})";
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
    public override string Description => $"CompositeSampler({string.Join(" AND ", _samplers.Select(s => s.Description))})";
}
