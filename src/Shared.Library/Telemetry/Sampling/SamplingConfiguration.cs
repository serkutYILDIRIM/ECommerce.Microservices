using OpenTelemetry.Trace;
using System.Diagnostics;

namespace Shared.Library.Telemetry.Sampling;

/// <summary>
/// Configuration for trace sampling
/// </summary>
public class SamplingConfiguration
{
    /// <summary>
    /// The type of sampler to use
    /// </summary>
    public SamplerType SamplerType { get; set; } = SamplerType.ParentBased;
    
    /// <summary>
    /// Sampling probability for traces (0.0-1.0)
    /// </summary>
    public double SamplingProbability { get; set; } = 0.1;
    
    /// <summary>
    /// Maximum number of traces per second (for rate limiting sampler)
    /// </summary>
    public int MaxTracesPerSecond { get; set; } = 100;
    
    /// <summary>
    /// Collection of rules for conditional sampling
    /// </summary>
    public List<SamplingRule> Rules { get; set; } = new();
    
    /// <summary>
    /// Whether to add composite sampling (combining different strategies)
    /// </summary>
    public bool UseCompositeSampling { get; set; } = false;
    
    /// <summary>
    /// Creates a default configuration with reasonable defaults
    /// </summary>
    /// <returns>Default sampling configuration</returns>
    public static SamplingConfiguration CreateDefault()
    {
        return new SamplingConfiguration
        {
            SamplerType = SamplerType.ParentBased,
            SamplingProbability = 0.1,
            MaxTracesPerSecond = 100,
            Rules = new List<SamplingRule>
            {
                // Always sample error spans
                new SamplingRule
                {
                    Name = "AlwaysSampleErrors",
                    AttributeMatches = new List<AttributeMatch>
                    {
                        new AttributeMatch { Key = "error", Value = "true" }
                    },
                    SamplingDecision = SamplingDecision.RecordAndSample
                },
                
                // Always sample slow spans
                new SamplingRule
                {
                    Name = "AlwaysSampleSlowOperations",
                    AttributeMatches = new List<AttributeMatch>
                    {
                        new AttributeMatch { Key = "db.operation.slow", Value = "true" }
                    },
                    SamplingDecision = SamplingDecision.RecordAndSample
                },
                
                // Always sample specific endpoints
                new SamplingRule
                {
                    Name = "CriticalEndpoints",
                    SpanNamePatterns = new List<string>
                    {
                        "*Order.Process*",
                        "*Payment*",
                        "*Checkout*"
                    },
                    SamplingDecision = SamplingDecision.RecordAndSample
                }
            }
        };
    }
    
    /// <summary>
    /// Creates a Sampler based on the configuration
    /// </summary>
    public Sampler CreateSampler(ILogger<ConditionalSampler> logger)
    {
        // Create the base sampler based on configuration
        Sampler baseSampler = SamplerType switch
        {
            SamplerType.AlwaysOn => new AlwaysOnSampler(),
            SamplerType.AlwaysOff => new AlwaysOffSampler(),
            SamplerType.TraceIdRatio => new TraceIdRatioBasedSampler(SamplingProbability),
            SamplerType.ParentBased => new ParentBasedRatioSampler(SamplingProbability),
            SamplerType.RateLimiting => new RateLimitingSampler(MaxTracesPerSecond),
            _ => new ParentBasedRatioSampler(SamplingProbability)
        };
        
        // If we have rules, use conditional sampler
        if (Rules.Count > 0)
        {
            baseSampler = new ConditionalSampler(baseSampler, Rules, logger);
        }
        
        // If composite sampling is enabled, combine with rate limiting
        if (UseCompositeSampling && SamplerType != SamplerType.RateLimiting)
        {
            var rateLimitingSampler = new RateLimitingSampler(MaxTracesPerSecond);
            baseSampler = new CompositeSampler(new List<Sampler> { baseSampler, rateLimitingSampler });
        }
        
        return baseSampler;
    }
}

/// <summary>
/// Different sampler types
/// </summary>
public enum SamplerType
{
    /// <summary>
    /// Always sample all traces
    /// </summary>
    AlwaysOn,
    
    /// <summary>
    /// Never sample any traces
    /// </summary>
    AlwaysOff,
    
    /// <summary>
    /// Sample based on trace ID with a probability
    /// </summary>
    TraceIdRatio,
    
    /// <summary>
    /// Sample based on parent decision with probability for root spans
    /// </summary>
    ParentBased,
    
    /// <summary>
    /// Sample based on a maximum rate
    /// </summary>
    RateLimiting
}

/// <summary>
/// Rule for determining if a trace should be sampled
/// </summary>
public class SamplingRule
{
    /// <summary>
    /// Name of the rule for identification
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Patterns to match against span names (supports * wildcard)
    /// </summary>
    public List<string> SpanNamePatterns { get; set; } = new();
    
    /// <summary>
    /// Attributes that must match for this rule to apply
    /// </summary>
    public List<AttributeMatch> AttributeMatches { get; set; } = new();
    
    /// <summary>
    /// SpanKinds that this rule applies to
    /// </summary>
    public List<SpanKind> SpanKinds { get; set; } = new();
    
    /// <summary>
    /// The sampling decision to make if this rule matches
    /// </summary>
    public SamplingDecision SamplingDecision { get; set; } = SamplingDecision.RecordAndSample;
}

/// <summary>
/// Attribute matching criteria for sampling rules
/// </summary>
public class AttributeMatch
{
    /// <summary>
    /// The attribute key to match
    /// </summary>
    public string Key { get; set; } = string.Empty;
    
    /// <summary>
    /// The value to match
    /// </summary>
    public string Value { get; set; } = string.Empty;
    
    /// <summary>
    /// Whether the value must exactly match (true) or contain (false)
    /// </summary>
    public bool ExactMatch { get; set; } = false;
}
