using System.Diagnostics;

namespace ProductCatalogService.Telemetry;

public static class TelemetryConfig
{
    // Constants for service information
    public const string ServiceName = "ProductCatalogService";
    public const string ServiceVersion = "1.0.0";
    
    // Create ActivitySource for manual instrumentation
    public static readonly ActivitySource ActivitySource = new(ServiceName, ServiceVersion);
}
