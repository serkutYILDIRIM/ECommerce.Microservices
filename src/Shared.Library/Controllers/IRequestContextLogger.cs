using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Trace;
using Shared.Library.Logging;
using System.Diagnostics;

namespace Shared.Library.Controllers;

/// <summary>
/// Interface for loggers that include request context
/// </summary>
public interface IRequestContextLogger
{
    /// <summary>
    /// Logs an operation with trace context
    /// </summary>
    void LogOperation<T>(LogLevel level, string message, T entity, params object[] args);
    
    /// <summary>
    /// Logs an exception with trace context
    /// </summary>
    void LogException(Exception ex, string message, params object[] args);
    
    /// <summary>
    /// Records and logs an entity-related operation
    /// </summary>
    void LogEntityOperation<T>(LogLevel level, string operation, T entity, string entityType = null) where T : class;
}

/// <summary>
/// Implementation of request context logger
/// </summary>
public class RequestContextLogger<T> : IRequestContextLogger where T : ControllerBase
{
    private readonly ILogger<T> _logger;
    private readonly IDiagnosticContextAccessor _diagnosticContext;
    
    public RequestContextLogger(ILogger<T> logger, IDiagnosticContextAccessor diagnosticContext)
    {
        _logger = logger;
        _diagnosticContext = diagnosticContext;
    }
    
    public void LogOperation<TEntity>(LogLevel level, string message, TEntity entity, params object[] args)
    {
        using (_diagnosticContext.PushActivityProperties())
        {
            AddEntityPropertiesToLogContext(entity);
            _logger.Log(level, message, args);
        }
    }
    
    public void LogException(Exception ex, string message, params object[] args)
    {
        using (_diagnosticContext.PushActivityProperties())
        {
            var activity = Activity.Current;
            if (activity != null)
            {
                activity.SetTag("error", true);
                activity.SetTag("error.type", ex.GetType().Name);
                activity.SetTag("error.message", ex.Message);
                activity.SetStatus(ActivityStatusCode.Error, ex.Message);
                activity.RecordException(ex);
            }
            
            _logger.LogError(ex, message, args);
        }
    }
    
    public void LogEntityOperation<TEntity>(LogLevel level, string operation, TEntity entity, string entityType = null) 
        where TEntity : class
    {
        using (_diagnosticContext.PushActivityProperties())
        {
            // Get entity ID if possible
            string entityId = GetEntityId(entity);
            string entityName = entityType ?? typeof(TEntity).Name;
            
            // Add to log context
            _diagnosticContext.Set("EntityType", entityName);
            _diagnosticContext.Set("EntityId", entityId);
            _diagnosticContext.Set("Operation", operation);
            
            // Add to activity tags if present
            var activity = Activity.Current;
            if (activity != null)
            {
                activity.SetTag("entity.type", entityName);
                activity.SetTag("entity.id", entityId);
                activity.SetTag("entity.operation", operation);
            }
            
            _logger.Log(level, "{Operation} {EntityType} with ID {EntityId}", operation, entityName, entityId);
        }
    }
    
    private void AddEntityPropertiesToLogContext<TEntity>(TEntity entity)
    {
        if (entity == null)
            return;
            
        // Get entity ID if possible
        string entityId = GetEntityId(entity);
        string entityName = typeof(TEntity).Name;
        
        // Add to log context
        _diagnosticContext.Set("EntityType", entityName);
        if (!string.IsNullOrEmpty(entityId))
            _diagnosticContext.Set("EntityId", entityId);
            
        // Add to activity tags if present
        var activity = Activity.Current;
        if (activity != null)
        {
            activity.SetTag("entity.type", entityName);
            if (!string.IsNullOrEmpty(entityId))
                activity.SetTag("entity.id", entityId);
        }
    }
    
    private string GetEntityId<TEntity>(TEntity entity)
    {
        if (entity == null)
            return "null";
            
        // Try to get ID property by common conventions
        var type = entity.GetType();
        
        // Try Id property
        var idProp = type.GetProperty("Id");
        if (idProp != null)
            return idProp.GetValue(entity)?.ToString() ?? "null";
            
        // Try {TypeName}Id property (e.g., ProductId)
        idProp = type.GetProperty($"{type.Name}Id");
        if (idProp != null)
            return idProp.GetValue(entity)?.ToString() ?? "null";
            
        // Return toString as fallback
        return entity.ToString() ?? "null";
    }
}

/// <summary>
/// Extension methods for adding request context logger
/// </summary>
public static class RequestContextLoggerExtensions
{
    /// <summary>
    /// Adds request context logger to the service collection
    /// </summary>
    public static IServiceCollection AddRequestContextLogger(this IServiceCollection services)
    {
        services.AddScoped(typeof(IRequestContextLogger), typeof(RequestContextLogger<>));
        
        return services;
    }
}
