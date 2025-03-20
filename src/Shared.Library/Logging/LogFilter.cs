using Serilog;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;
using Serilog.Filters;
using System.Diagnostics;

namespace Shared.Library.Logging;

/// <summary>
/// Provides filtering capabilities for structured logging
/// </summary>
public static class LogFilter
{
    /// <summary>
    /// Enriches the logger configuration with filtering capabilities
    /// </summary>
    public static LoggerConfiguration WithFiltering(
        this LoggerConfiguration loggerConfiguration,
        IConfiguration configuration)
    {
        // Get filter configuration
        var filterConfig = configuration.GetSection("Serilog:Filtering");
        
        // Apply exclusion filter if provided
        var excludeByPropertyFilter = filterConfig?.GetSection("ExcludeByProperty")?.Get<List<PropertyFilter>>();
        if (excludeByPropertyFilter != null && excludeByPropertyFilter.Any())
        {
            foreach (var filter in excludeByPropertyFilter)
            {
                loggerConfiguration.Filter.ByExcluding(
                    Matching.WithProperty(filter.Name, filter.Values));
            }
        }
        
        // Apply path exclusion for noisy endpoints
        var excludePaths = filterConfig?.GetSection("ExcludePaths")?.Get<List<string>>();
        if (excludePaths != null && excludePaths.Any())
        {
            loggerConfiguration.Filter.ByExcluding(evt => 
                evt.Properties.TryGetValue("RequestPath", out var path) && 
                excludePaths.Any(excluded => path.ToString().Contains(excluded, StringComparison.OrdinalIgnoreCase)));
        }
        
        // Apply category-specific levels
        var categoryLevels = filterConfig?.GetSection("CategoryLevels")?.Get<Dictionary<string, string>>();
        if (categoryLevels != null && categoryLevels.Any())
        {
            foreach (var category in categoryLevels)
            {
                if (Enum.TryParse<LogEventLevel>(category.Value, true, out var level))
                {
                    loggerConfiguration.Filter.ByIncludingOnly(
                        Matching.WithProperty("SourceContext", sc => 
                            sc.ToString().StartsWith(category.Key)) && 
                        Matching.FromSource(category.Key));
                }
            }
        }
        
        // Apply minimum levels for third-party libraries
        if (filterConfig?.GetSection("OverrideMinimumLevel") != null)
        {
            loggerConfiguration.MinimumLevel.Override("Microsoft", GetLogEventLevel(
                filterConfig["OverrideMinimumLevel:Microsoft"], LogEventLevel.Warning));
                
            loggerConfiguration.MinimumLevel.Override("System", GetLogEventLevel(
                filterConfig["OverrideMinimumLevel:System"], LogEventLevel.Warning));
                
            loggerConfiguration.MinimumLevel.Override("Microsoft.AspNetCore.Hosting", GetLogEventLevel(
                filterConfig["OverrideMinimumLevel:Microsoft.AspNetCore.Hosting"], LogEventLevel.Information));
                
            // Apply any other custom overrides
            var customOverrides = filterConfig.GetSection("OverrideMinimumLevel:Custom")?.Get<Dictionary<string, string>>();
            if (customOverrides != null)
            {
                foreach (var custom in customOverrides)
                {
                    loggerConfiguration.MinimumLevel.Override(
                        custom.Key, 
                        GetLogEventLevel(custom.Value, LogEventLevel.Information));
                }
            }
        }
        
        return loggerConfiguration;
    }
    
    // Helper to parse log level with fallback
    private static LogEventLevel GetLogEventLevel(string? levelName, LogEventLevel defaultLevel)
    {
        if (string.IsNullOrEmpty(levelName))
            return defaultLevel;
            
        return Enum.TryParse<LogEventLevel>(levelName, true, out var level) 
            ? level 
            : defaultLevel;
    }
}

/// <summary>
/// Defines a property-based filter
/// </summary>
public class PropertyFilter
{
    public string Name { get; set; } = string.Empty;
    public List<string> Values { get; set; } = new List<string>();
}

/// <summary>
/// Filter for logs with specific traits and context
/// </summary>
public class LogTraitFilter : ILogEventFilter
{
    private readonly string _categoryPrefix;
    private readonly LogEventLevel _minimumLevel;
    
    public LogTraitFilter(string categoryPrefix, LogEventLevel minimumLevel)
    {
        _categoryPrefix = categoryPrefix;
        _minimumLevel = minimumLevel;
    }
    
    public bool IsEnabled(LogEvent logEvent)
    {
        // Apply category-based filtering
        if (!string.IsNullOrEmpty(_categoryPrefix) && 
            logEvent.Properties.TryGetValue("SourceContext", out var sourceContext))
        {
            var category = sourceContext.ToString().Trim('"');
            if (category.StartsWith(_categoryPrefix))
            {
                return logEvent.Level >= _minimumLevel;
            }
        }
        
        return true; // Don't filter if not matching our criteria
    }
}

/// <summary>
/// Extension methods for filtering enrichment
/// </summary>
public static class LogFilterExtensions
{
    /// <summary>
    /// Adds a category-specific log level filter
    /// </summary>
    public static LoggerConfiguration WithCategoryFilter(
        this LoggerFilterConfiguration filterConfiguration,
        string categoryPrefix, 
        LogEventLevel minimumLevel)
    {
        return filterConfiguration.With(new LogTraitFilter(categoryPrefix, minimumLevel));
    }
}
