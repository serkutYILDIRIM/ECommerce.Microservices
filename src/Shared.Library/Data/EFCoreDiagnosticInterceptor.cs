using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Shared.Library.Data;

/// <summary>
/// Interceptor for entity framework that tracks detailed execution metrics and emits
/// span events for detailed query tracing
/// </summary>
public class EFCoreDiagnosticInterceptor : DbCommandInterceptor
{
    private readonly string _serviceName;
    private readonly ActivitySource _activitySource;
    private readonly ILogger<EFCoreDiagnosticInterceptor> _logger;

    public EFCoreDiagnosticInterceptor(
        string serviceName,
        ActivitySource activitySource,
        ILogger<EFCoreDiagnosticInterceptor> logger)
    {
        _serviceName = serviceName;
        _activitySource = activitySource;
        _logger = logger;
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        // Track command execution start
        command.CommandTimeout = 30; // Set a reasonable timeout
        return result;
    }

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        // Track command execution start for async operations
        command.CommandTimeout = 30; // Set a reasonable timeout
        return result;
    }

    public override DbDataReader ReaderExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result)
    {
        RecordCommand(command, eventData, "Synchronous");
        return result;
    }

    public override async ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        RecordCommand(command, eventData, "Asynchronous");
        return result;
    }

    private void RecordCommand(DbCommand command, CommandExecutedEventData eventData, string executionType)
    {
        var currentActivity = Activity.Current;
        
        if (currentActivity != null)
        {
            // Create command details for tracing
            var commandText = command.CommandText;
            var commandType = command.CommandType.ToString();
            var executionTime = eventData.Duration.TotalMilliseconds;
            var database = command.Connection?.Database ?? "unknown";
            var dataSource = command.Connection?.DataSource ?? "unknown";
            
            // Add basic tags
            currentActivity.SetTag("db.type", "ef_core");
            currentActivity.SetTag("db.system", database);
            currentActivity.SetTag("db.command_type", commandType);
            currentActivity.SetTag("db.execution_time_ms", executionTime);
            
            // Add detailed command information
            var tags = new ActivityTagsCollection
            {
                { "db.execution_type", executionType },
                { "db.execution_time_ms", executionTime },
                { "db.command_type", commandType },
                { "db.database", database },
                { "db.data_source", dataSource }
            };
            
            // Add exception information if any
            if (eventData.Exception != null)
            {
                tags.Add("db.error", eventData.Exception.Message);
                tags.Add("db.error_type", eventData.Exception.GetType().Name);
                currentActivity.SetStatus(ActivityStatusCode.Error, eventData.Exception.Message);
            }
            
            // Record full command details as an event
            currentActivity.AddEvent(new ActivityEvent("EFCoreCommandExecuted", tags: tags));
            
            // Log slow queries for investigation
            if (executionTime > 1000) // 1 second threshold for slow queries
            {
                _logger.LogWarning("Slow database query detected: {ExecutionTime}ms, Command: {CommandType}, Database: {Database}", 
                    executionTime, commandType, database);
            }
        }
    }
}
