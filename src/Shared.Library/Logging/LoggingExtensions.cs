using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Enrichers.Span;
using Serilog.Events;
using Serilog.Expressions;
using Serilog.Formatting.Compact;
using Serilog.Formatting.Json;
using System.Diagnostics;

namespace Shared.Library.Logging;

public static class LoggingExtensions
{
    /// <summary>
    /// Adds structured logging with trace context correlation to the application
    /// </summary>
    public static WebApplicationBuilder AddStructuredLogging(
        this WebApplicationBuilder builder, 
        string serviceName,
        string serviceVersion)
    {
        // Get base log level from configuration
        var defaultLogLevel = GetLogEventLevel(
            builder.Configuration["Serilog:MinimumLevel:Default"], 
            LogEventLevel.Information);

        // Create Serilog logger configuration
        var loggerConfiguration = new LoggerConfiguration()
            .MinimumLevel.Is(defaultLogLevel)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("ServiceName", serviceName)
            .Enrich.WithProperty("ServiceVersion", serviceVersion)
            .Enrich.WithProperty("Environment", builder.Environment.EnvironmentName)
            .Enrich.WithProperty("ApplicationName", builder.Environment.ApplicationName)
            .Enrich.WithMachineName()
            .Enrich.WithProcessId()
            .Enrich.WithThreadId()
            // Adds trace/span ID enrichment
            .Enrich.WithSpan(new SpanOptions 
            { 
                IncludeOperationName = true,
                IncludeTags = true,
                IncludeTraceFlags = true,
                IncludeSpecificTags = new[] { "http.method", "http.url", "http.status_code", "error" }
            })
            // Adds OpenTelemetry baggage
            .Enrich.With(new BaggageEnricher(includeAllBaggageItems: true));

        // Apply standard minimum level overrides
        ConfigureMinimumLevelOverrides(loggerConfiguration, builder.Configuration);

        // Apply advanced filtering
        loggerConfiguration.WithFiltering(builder.Configuration);

        // Add console logging
        if (builder.Environment.IsDevelopment())
        {
            // Use more detailed output in development
            loggerConfiguration.WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3} {SourceContext}] {Message:lj} {NewLine}{Properties}{NewLine}{Exception}");
        }
        else
        {
            // Use more compact output in production
            loggerConfiguration.WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3} {TraceId:0:x}{SpanId:0:x}] {Message:lj} {Properties}{NewLine}{Exception}");
        }

        // Configure log sinks based on environment
        ConfigureLogSinks(loggerConfiguration, builder);
        
        // Configure Elasticsearch sink if configured
        loggerConfiguration.ConfigureElasticsearch(
            builder.Configuration, 
            serviceName, 
            serviceVersion, 
            builder.Environment.EnvironmentName);

        // Read detailed configuration from appsettings
        loggerConfiguration.ReadFrom.Configuration(builder.Configuration);

        // Create logger
        Log.Logger = loggerConfiguration.CreateLogger();

        // Use Serilog as the logging provider
        builder.Host.UseSerilog((ctx, services, configuration) => 
        {
            // Start with the basic configuration
            configuration
                .MinimumLevel.Is(defaultLogLevel)
                .ReadFrom.Configuration(ctx.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("ServiceName", serviceName)
                .Enrich.WithProperty("ServiceVersion", serviceVersion)
                .Enrich.WithProperty("Environment", builder.Environment.EnvironmentName)
                .Enrich.WithProperty("ApplicationName", builder.Environment.ApplicationName)
                .Enrich.WithMachineName()
                .Enrich.WithProcessId()
                .Enrich.WithThreadId()
                .Enrich.WithSpan(new SpanOptions 
                { 
                    IncludeOperationName = true,
                    IncludeTags = true,
                    IncludeTraceFlags = true,
                    IncludeSpecificTags = new[] { "http.method", "http.url", "http.status_code", "error" }
                })
                .Enrich.With(new BaggageEnricher(includeAllBaggageItems: true));

            // Apply standard minimum level overrides
            ConfigureMinimumLevelOverrides(configuration, ctx.Configuration);

            // Apply advanced filtering
            configuration.WithFiltering(ctx.Configuration);

            // Configure console output
            if (builder.Environment.IsDevelopment())
            {
                configuration.WriteTo.Console(
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3} {SourceContext}] {Message:lj} {NewLine}{Properties}{NewLine}{Exception}");
            }
            else
            {
                configuration.WriteTo.Console(
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3} {TraceId:0:x}{SpanId:0:x}] {Message:lj} {Properties}{NewLine}{Exception}");
            }

            // Configure log sinks
            ConfigureLogSinks(configuration, builder);
            
            // Configure Elasticsearch sink if configured
            configuration.ConfigureElasticsearch(
                ctx.Configuration, 
                serviceName, 
                serviceVersion, 
                builder.Environment.EnvironmentName);
        }, dispose: true);

        // Use custom trace listener to correlate console output with traces
        Trace.Listeners.Add(new SerilogTraceListener());

        // Add diagnostic context accessor for linking traces with logs
        builder.Services.AddSingleton<IDiagnosticContextAccessor, DiagnosticContextAccessor>();

        return builder;
    }
    
    // Configure log level overrides
    private static void ConfigureMinimumLevelOverrides(LoggerConfiguration loggerConfiguration, IConfiguration config)
    {
        // Apply standard overrides
        loggerConfiguration
            .MinimumLevel.Override("Microsoft", GetLogEventLevel(
                config["Serilog:MinimumLevel:Override:Microsoft"], LogEventLevel.Warning))
            .MinimumLevel.Override("System", GetLogEventLevel(
                config["Serilog:MinimumLevel:Override:System"], LogEventLevel.Warning))
            .MinimumLevel.Override("Microsoft.Hosting.Lifetime", GetLogEventLevel(
                config["Serilog:MinimumLevel:Override:Microsoft.Hosting.Lifetime"], LogEventLevel.Information))
            .MinimumLevel.Override("Microsoft.AspNetCore", GetLogEventLevel(
                config["Serilog:MinimumLevel:Override:Microsoft.AspNetCore"], LogEventLevel.Warning))
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", GetLogEventLevel(
                config["Serilog:MinimumLevel:Override:Microsoft.EntityFrameworkCore"], LogEventLevel.Warning));

        // Get custom overrides
        var overrides = config.GetSection("Serilog:MinimumLevel:Override").GetChildren();
        foreach (var override_ in overrides)
        {
            var sourceContext = override_.Key;
            if (!IsStandardOverride(sourceContext))
            {
                if (Enum.TryParse<LogEventLevel>(override_.Value, true, out var level))
                {
                    loggerConfiguration.MinimumLevel.Override(sourceContext, level);
                }
            }
        }
    }
    
    // Helper to check if a source context is already handled
    private static bool IsStandardOverride(string sourceContext)
    {
        return sourceContext == "Microsoft" || 
               sourceContext == "System" || 
               sourceContext == "Microsoft.Hosting.Lifetime" ||
               sourceContext == "Microsoft.AspNetCore" ||
               sourceContext == "Microsoft.EntityFrameworkCore";
    }
    
    // Configure log sinks based on environment and configuration
    private static void ConfigureLogSinks(LoggerConfiguration loggerConfiguration, WebApplicationBuilder builder)
    {
        // Add file logging in non-development environments
        if (!builder.Environment.IsDevelopment())
        {
            var serviceName = builder.Configuration["ServiceInfo:Name"] ?? 
                              builder.Environment.ApplicationName.ToLowerInvariant();
                              
            // Add structured JSON logs
            loggerConfiguration.WriteTo.File(
                new CompactJsonFormatter(), 
                $"logs/{serviceName}.json", 
                rollingInterval: RollingInterval.Day, 
                retainedFileCountLimit: 7);
                
            // Also add plain text logs for easier reading if needed
            loggerConfiguration.WriteTo.File(
                $"logs/{serviceName}_plain.txt",
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] {Message:lj} {Properties}{NewLine}{Exception}",
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7);
        }

        // Add Seq logging if configured
        var seqServerUrl = builder.Configuration["Logging:Seq:ServerUrl"];
        if (!string.IsNullOrEmpty(seqServerUrl))
        {
            loggerConfiguration.WriteTo.Seq(
                seqServerUrl,
                apiKey: builder.Configuration["Logging:Seq:ApiKey"],
                restrictedToMinimumLevel: GetLogEventLevel(
                    builder.Configuration["Logging:Seq:MinimumLevel"], 
                    LogEventLevel.Information));
        }
    }
    
    /// <summary>
    /// Adds diagnostic context logging for controllers and other components
    /// </summary>
    public static IServiceCollection AddDiagnosticContext(
        this IServiceCollection services)
    {
        // Add diagnostic context accessor
        services.AddSingleton<IDiagnosticContextAccessor, DiagnosticContextAccessor>();
        
        return services;
    }
    
    /// <summary>
    /// Helper to parse log level from string with fallback
    /// </summary>
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
/// Interface for accessing diagnostic context
/// </summary>
public interface IDiagnosticContextAccessor
{
    /// <summary>
    /// Sets a property in the diagnostic context
    /// </summary>
    void Set(string propertyName, object value);
    
    /// <summary>
    /// Gets the current trace ID
    /// </summary>
    string? GetTraceId();
    
    /// <summary>
    /// Gets the current span ID
    /// </summary>
    string? GetSpanId();
    
    /// <summary>
    /// Pushes all properties from the current Activity to the log context
    /// </summary>
    IDisposable PushActivityProperties();
}

/// <summary>
/// Implementation of diagnostic context accessor
/// </summary>
public class DiagnosticContextAccessor : IDiagnosticContextAccessor
{
    public void Set(string propertyName, object value)
    {
        LogContext.PushProperty(propertyName, value);
    }
    
    public string? GetTraceId()
    {
        return Activity.Current?.TraceId.ToString();
    }
    
    public string? GetSpanId()
    {
        return Activity.Current?.SpanId.ToString();
    }
    
    public IDisposable PushActivityProperties()
    {
        var activity = Activity.Current;
        if (activity == null)
            return new DummyDisposable();
            
        // Create properties to push
        var properties = new List<IDisposable>
        {
            LogContext.PushProperty("TraceId", activity.TraceId.ToString()),
            LogContext.PushProperty("SpanId", activity.SpanId.ToString())
        };
        
        if (activity.ParentSpanId != default)
            properties.Add(LogContext.PushProperty("ParentSpanId", activity.ParentSpanId.ToString()));
            
        if (!string.IsNullOrEmpty(activity.OperationName))
            properties.Add(LogContext.PushProperty("OperationName", activity.OperationName));
            
        // Add key baggage items
        foreach (var item in activity.Baggage)
        {
            properties.Add(LogContext.PushProperty($"Baggage_{item.Key}", item.Value));
        }
        
        return new CompositeDisposable(properties);
    }
    
    private class DummyDisposable : IDisposable
    {
        public void Dispose() { }
    }
    
    private class CompositeDisposable : IDisposable
    {
        private readonly List<IDisposable> _disposables;
        
        public CompositeDisposable(List<IDisposable> disposables)
        {
            _disposables = disposables;
        }
        
        public void Dispose()
        {
            foreach (var disposable in _disposables)
            {
                disposable.Dispose();
            }
        }
    }
}

/// <summary>
/// Trace listener that redirects Debug.WriteLine and Trace.WriteLine to Serilog
/// </summary>
public class SerilogTraceListener : TraceListener
{
    public override void Write(string? message)
    {
        if (message != null)
            Log.Debug(message);
    }

    public override void WriteLine(string? message)
    {
        if (message != null)
            Log.Debug(message);
    }
}
