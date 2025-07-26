using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Trace;
using Shared.Library.Metrics;

namespace Shared.Library.Data;

public static class DataExtensions
{
    /// <summary>
    /// Adds Entity Framework Core tracing and performance monitoring to a DbContext
    /// </summary>
    public static IServiceCollection AddEFCoreTracing<TContext>(
        this IServiceCollection services, 
        string serviceName,
        ActivitySource activitySource,
        Action<DbContextOptionsBuilder>? optionsAction = null) 
        where TContext : DbContext
    {
        // Register the interceptor
        services.AddSingleton<EFCoreDiagnosticInterceptor>(sp => 
            new EFCoreDiagnosticInterceptor(
                serviceName,
                activitySource,
                sp.GetRequiredService<ILogger<EFCoreDiagnosticInterceptor>>()));

        // Configure the DbContext with the interceptor
        services.AddDbContext<TContext>((sp, options) =>
        {
            // Apply custom options if provided
            optionsAction?.Invoke(options);
            
            // Add the diagnostic interceptor
            var interceptor = sp.GetRequiredService<EFCoreDiagnosticInterceptor>();
            options.AddInterceptors(interceptor);
            
            // Enable detailed errors and sensitive data logging in development
            var env = sp.GetService<IHostEnvironment>();
            if (env?.IsDevelopment() ?? false)
            {
                options.EnableDetailedErrors();
                options.EnableSensitiveDataLogging();
            }
        });

        return services;
    }
    
    /// <summary>
    /// Extension method for DbContext to track query performance
    /// </summary>
    public static async Task<List<T>> ToTrackedListAsync<T>(
        this IQueryable<T> query, 
        string operationName,
        ActivitySource activitySource,
        CancellationToken cancellationToken = default)
    {
        // Get entity type name for metrics
        var entityType = typeof(T).Name;
        
        // Create a span for this specific query
        using var activity = activitySource.StartActivity($"DB:{operationName}");
        
        try
        {
            // Measure query execution time
            var stopwatch = Stopwatch.StartNew();
            var result = await query.ToListAsync(cancellationToken);
            stopwatch.Stop();
            
            // Record metrics
            activity?.SetTag("db.operation", operationName);
            activity?.SetTag("db.entity_type", entityType);
            activity?.SetTag("db.result_count", result.Count);
            activity?.SetTag("db.execution_time_ms", stopwatch.ElapsedMilliseconds);
            
            // Get performance metrics service if available
            var performanceMetrics = GetPerformanceMetrics();
            performanceMetrics?.RecordEntityFrameworkOperation(
                operationName, 
                entityType, 
                stopwatch.ElapsedMilliseconds, 
                true);
            
            return result;
        }
        catch (Exception ex)
        {
            // Record exception information
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.RecordException(ex);
            
            // Record failure metrics
            var performanceMetrics = GetPerformanceMetrics();
            performanceMetrics?.RecordEntityFrameworkOperation(
                operationName, 
                entityType, 
                -1, // Unable to measure time for failed operations
                false);
                
            throw;
        }
        
        // Helper to get performance metrics if available
        PerformanceMetrics? GetPerformanceMetrics()
        {
            try 
            {
                using var scope = GetServiceProvider()?.CreateScope();
                return scope?.ServiceProvider.GetService<PerformanceMetrics>();
            }
            catch
            {
                return null;
            }
        }
        
        // Helper to get service provider from current Activity
        IServiceProvider? GetServiceProvider()
        {
            try
            {
                return Activity.Current?.GetServiceProvider();
            }
            catch
            {
                return null;
            }
        }
    }
    
    /// <summary>
    /// Extension method to track individual entity retrieval performance
    /// </summary>
    public static async Task<T?> ToTrackedFirstOrDefaultAsync<T>(
        this IQueryable<T> query,
        string operationName,
        ActivitySource activitySource,
        CancellationToken cancellationToken = default)
    {
        // Get entity type name for metrics
        var entityType = typeof(T).Name;
        
        // Create a span for this specific query
        using var activity = activitySource.StartActivity($"DB:{operationName}");
        
        try
        {
            // Measure query execution time
            var stopwatch = Stopwatch.StartNew();
            var result = await query.FirstOrDefaultAsync(cancellationToken);
            stopwatch.Stop();
            
            // Record metrics
            activity?.SetTag("db.operation", operationName);
            activity?.SetTag("db.entity_type", entityType);
            activity?.SetTag("db.found_result", result != null);
            activity?.SetTag("db.execution_time_ms", stopwatch.ElapsedMilliseconds);
            
            // Get performance metrics service if available
            var performanceMetrics = GetPerformanceMetrics();
            performanceMetrics?.RecordEntityFrameworkOperation(
                operationName, 
                entityType, 
                stopwatch.ElapsedMilliseconds, 
                true);
            
            return result;
        }
        catch (Exception ex)
        {
            // Record exception information
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.RecordException(ex);
            
            // Record failure metrics
            var performanceMetrics = GetPerformanceMetrics();
            performanceMetrics?.RecordEntityFrameworkOperation(
                operationName, 
                entityType, 
                -1, // Unable to measure time for failed operations
                false);
                
            throw;
        }
        
        // Helper to get performance metrics if available
        PerformanceMetrics? GetPerformanceMetrics()
        {
            try 
            {
                using var scope = GetServiceProvider()?.CreateScope();
                return scope?.ServiceProvider.GetService<PerformanceMetrics>();
            }
            catch
            {
                return null;
            }
        }
        
        // Helper to get service provider from current Activity
        IServiceProvider? GetServiceProvider()
        {
            try
            {
                return Activity.Current?.GetServiceProvider();
            }
            catch
            {
                return null;
            }
        }
    }
}

/// <summary>
/// Extensions for Activity to help integrate with DI
/// </summary>
public static class ActivityExtensions
{
    private const string ServiceProviderKey = "ServiceProvider";

    public static Activity SetServiceProvider(this Activity activity, IServiceProvider serviceProvider)
    {
        var id = ActivityServiceProviderRegistry.RegisterProvider(serviceProvider);
        activity.SetTag(ServiceProviderKey, id);
        return activity;
    }


    public static IServiceProvider? GetServiceProvider(this Activity? activity)
    {
        if (activity == null) return null;

        // Get the tag value as a string (possibly containing a reference ID)
        var tagValue = activity.Tags.FirstOrDefault(t => t.Key == ServiceProviderKey).Value;

        // We need to use a mechanism to translate from the tag string to the actual service provider
        // This could involve a static dictionary or some other storage mechanism
        if (!string.IsNullOrEmpty(tagValue) && ActivityServiceProviderRegistry.TryGetProvider(tagValue, out var serviceProvider))
            return serviceProvider;

        return null;
    }

    /// <summary>
    /// Registry to store service provider references that can be retrieved by string identifiers
    /// </summary>
    public static class ActivityServiceProviderRegistry
    {
        private static readonly Dictionary<string, IServiceProvider> _providers = new();
        private static readonly object _lock = new();

        public static string RegisterProvider(IServiceProvider provider)
        {
            string id = Guid.NewGuid().ToString("N");
            lock (_lock)
            {
                _providers[id] = provider;
            }
            return id;
        }

        public static bool TryGetProvider(string id, out IServiceProvider? provider)
        {
            lock (_lock)
            {
                return _providers.TryGetValue(id, out provider);
            }
        }

        public static void RemoveProvider(string id)
        {
            lock (_lock)
            {
                _providers.Remove(id);
            }
        }
    }


}
