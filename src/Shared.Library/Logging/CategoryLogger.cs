using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Shared.Library.Logging;

/// <summary>
/// Provides a way to log with specific categories
/// </summary>
public static class CategoryLogger
{
    // Predefined categories for organizational purposes
    public static class Categories
    {
        public const string General = "General";
        public const string Security = "Security";
        public const string Performance = "Performance";
        public const string BusinessLogic = "BusinessLogic";
        public const string DataAccess = "DataAccess";
        public const string API = "API";
        public const string BackgroundService = "BackgroundService";
        public const string Integration = "Integration";
        public const string Telemetry = "Telemetry";
        public const string Infrastructure = "Infrastructure";
    }

    /// <summary>
    /// Log with a specific category
    /// </summary>
    public static void LogWithCategory(
        this ILogger logger, 
        LogLevel logLevel, 
        string category,
        string message, 
        params object[] args)
    {
        using (LogContext.PushProperty("Category", category))
        {
            logger.Log(logLevel, message, args);
        }
    }
    
    /// <summary>
    /// Log with a specific category and include exception
    /// </summary>
    public static void LogWithCategory(
        this ILogger logger, 
        LogLevel logLevel, 
        string category,
        Exception exception,
        string message, 
        params object[] args)
    {
        using (LogContext.PushProperty("Category", category))
        {
            logger.Log(logLevel, exception, message, args);
            
            // Also add category to the activity if present
            if (Activity.Current != null)
            {
                Activity.Current.SetTag("log.category", category);
            }
        }
    }
    
    #region Convenience Methods
    
    // Convenience methods for different log levels
    public static void SecurityError(this ILogger logger, string message, params object[] args) => 
        logger.LogWithCategory(LogLevel.Error, Categories.Security, message, args);
        
    public static void SecurityWarning(this ILogger logger, string message, params object[] args) => 
        logger.LogWithCategory(LogLevel.Warning, Categories.Security, message, args);
        
    public static void SecurityInfo(this ILogger logger, string message, params object[] args) => 
        logger.LogWithCategory(LogLevel.Information, Categories.Security, message, args);
        
    public static void BusinessError(this ILogger logger, string message, params object[] args) => 
        logger.LogWithCategory(LogLevel.Error, Categories.BusinessLogic, message, args);
        
    public static void BusinessWarning(this ILogger logger, string message, params object[] args) => 
        logger.LogWithCategory(LogLevel.Warning, Categories.BusinessLogic, message, args);
        
    public static void BusinessInfo(this ILogger logger, string message, params object[] args) => 
        logger.LogWithCategory(LogLevel.Information, Categories.BusinessLogic, message, args);
        
    public static void DataError(this ILogger logger, string message, params object[] args) => 
        logger.LogWithCategory(LogLevel.Error, Categories.DataAccess, message, args);
        
    public static void DataWarning(this ILogger logger, string message, params object[] args) => 
        logger.LogWithCategory(LogLevel.Warning, Categories.DataAccess, message, args);
        
    public static void DataInfo(this ILogger logger, string message, params object[] args) => 
        logger.LogWithCategory(LogLevel.Information, Categories.DataAccess, message, args);
        
    public static void APIInfo(this ILogger logger, string message, params object[] args) => 
        logger.LogWithCategory(LogLevel.Information, Categories.API, message, args);
        
    public static void APIDebug(this ILogger logger, string message, params object[] args) => 
        logger.LogWithCategory(LogLevel.Debug, Categories.API, message, args);
        
    public static void PerformanceInfo(this ILogger logger, string message, params object[] args) => 
        logger.LogWithCategory(LogLevel.Information, Categories.Performance, message, args);
    
    #endregion
}

/// <summary>
/// Simple class to push properties into the log context
/// </summary>
public class LogContext : IDisposable
{
    private readonly List<IDisposable> _disposables = new();
    
    private LogContext() { }
    
    public static LogContext PushProperty(string name, object? value)
    {
        var context = new LogContext();
        context._disposables.Add(Serilog.Context.LogContext.PushProperty(name, value));
        return context;
    }
    
    public void Dispose()
    {
        foreach (var disposable in _disposables)
        {
            disposable.Dispose();
        }
    }
}
