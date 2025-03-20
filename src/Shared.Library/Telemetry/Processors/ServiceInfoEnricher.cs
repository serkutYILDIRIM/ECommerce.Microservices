using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Shared.Library.Telemetry.Processors;

/// <summary>
/// Enriches spans with service information
/// </summary>
public class ServiceInfoEnricher : ISpanEnricher
{
    private readonly string _serviceName;
    private readonly string _serviceVersion;
    private readonly string _environment;
    private readonly ILogger<ServiceInfoEnricher> _logger;
    
    // Cached environment information
    private readonly Dictionary<string, string> _environmentInfo;

    public ServiceInfoEnricher(
        string serviceName, 
        string serviceVersion, 
        string environment, 
        ILogger<ServiceInfoEnricher> logger)
    {
        _serviceName = serviceName;
        _serviceVersion = serviceVersion;
        _environment = environment;
        _logger = logger;
        
        // Initialize environment information
        _environmentInfo = new Dictionary<string, string>
        {
            ["service.name"] = serviceName,
            ["service.version"] = serviceVersion,
            ["service.environment"] = environment,
            ["service.instance.id"] = Environment.MachineName,
            ["host.name"] = Environment.MachineName,
            ["os.type"] = RuntimeInformation.OSDescription,
            ["os.version"] = Environment.OSVersion.ToString(),
            ["runtime.name"] = RuntimeInformation.FrameworkDescription,
            ["runtime.version"] = Environment.Version.ToString(),
            ["deployment.environment"] = environment
        };
    }

    /// <summary>
    /// Add service information to spans at start time
    /// </summary>
    public void EnrichSpanAtStart(Activity span)
    {
        // Only add service info to root spans or spans without parent (to avoid redundancy)
        if (span.Parent == null || span.ParentSpanId == default)
        {
            // Add basic service information
            foreach (var kvp in _environmentInfo)
            {
                span.SetTag(kvp.Key, kvp.Value);
            }
        }
        
        // Add baggage information
        span.SetTag("service.name", _serviceName);
        
        // Always add to baggage for context propagation
        span.AddBaggage("service.name", _serviceName);
        span.AddBaggage("service.environment", _environment);
    }

    /// <summary>
    /// Nothing to add at span end for service information
    /// </summary>
    public void EnrichSpanAtEnd(Activity span)
    {
        // No additional service info needed at end
    }
}
