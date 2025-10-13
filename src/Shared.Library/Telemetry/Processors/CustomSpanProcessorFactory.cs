using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Shared.Library.Telemetry.Processors;

/// <summary>
/// Factory for creating and configuring custom span processors
/// </summary>
public static class CustomSpanProcessorFactory
{
    /// <summary>
    /// Creates a custom span processor with standard enrichers for web applications
    /// </summary>
    public static CustomSpanProcessor CreateWebSpanProcessor(
        string serviceName,
        string serviceVersion,
        string environment,
        IHttpContextAccessor httpContextAccessor,
        ILoggerFactory loggerFactory)
    {
        // Create loggers for each component
        var processorLogger = loggerFactory.CreateLogger<CustomSpanProcessor>();
        var serviceInfoLogger = loggerFactory.CreateLogger<ServiceInfoEnricher>();
        var httpContextLogger = loggerFactory.CreateLogger<HttpContextEnricher>();
        var databaseLogger = loggerFactory.CreateLogger<DatabaseOperationEnricher>();
        var errorDetailsLogger = loggerFactory.CreateLogger<ErrorDetailsEnricher>();
        
        // Create enrichers
        var enrichers = new List<ISpanEnricher>
        {
            new ServiceInfoEnricher(serviceName, serviceVersion, environment, serviceInfoLogger),
            new HttpContextEnricher(httpContextAccessor, httpContextLogger),
            new DatabaseOperationEnricher(databaseLogger),
            new ErrorDetailsEnricher(errorDetailsLogger)
        };
        
        // Create processor with all enrichers
        return new CustomSpanProcessor(enrichers, processorLogger);
    }
    
    /// <summary>
    /// Creates a custom span processor with standard enrichers for worker services
    /// </summary>
    public static CustomSpanProcessor CreateWorkerSpanProcessor(
        string serviceName,
        string serviceVersion,
        string environment,
        ILoggerFactory loggerFactory)
    {
        // Create loggers for each component
        var processorLogger = loggerFactory.CreateLogger<CustomSpanProcessor>();
        var serviceInfoLogger = loggerFactory.CreateLogger<ServiceInfoEnricher>();
        var databaseLogger = loggerFactory.CreateLogger<DatabaseOperationEnricher>();
        var errorDetailsLogger = loggerFactory.CreateLogger<ErrorDetailsEnricher>();
        
        // Create enrichers (without HTTP context for workers)
        var enrichers = new List<ISpanEnricher>
        {
            new ServiceInfoEnricher(serviceName, serviceVersion, environment, serviceInfoLogger),
            new DatabaseOperationEnricher(databaseLogger),
            new ErrorDetailsEnricher(errorDetailsLogger)
        };
        
        // Create processor with all enrichers
        return new CustomSpanProcessor(enrichers, processorLogger);
    }
}
