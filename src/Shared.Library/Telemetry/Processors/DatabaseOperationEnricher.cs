using System.Diagnostics;

namespace Shared.Library.Telemetry.Processors;

/// <summary>
/// Enriches database-related spans with additional information
/// </summary>
public class DatabaseOperationEnricher : ISpanEnricher
{
    private readonly ILogger<DatabaseOperationEnricher> _logger;

    public DatabaseOperationEnricher(ILogger<DatabaseOperationEnricher> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Identifies and enriches database spans at start
    /// </summary>
    public void EnrichSpanAtStart(Activity span)
    {
        if (!IsDatabaseOperation(span)) return;
        
        try
        {
            // Add DB type classification
            if (IsDatabaseActivityType(span, "SqlClient"))
            {
                span.SetTag("db.system", "mssql");
                span.SetTag("db.technology", "Microsoft SQL Server");
            }
            else if (IsDatabaseActivityType(span, "EntityFrameworkCore"))
            {
                span.SetTag("db.system", "entityframework");
                span.SetTag("db.technology", "Entity Framework Core");
                
                // Extract entity information from EF Core spans
                if (span.DisplayName.Contains("SaveChanges"))
                {
                    span.SetTag("db.operation", "write");
                    span.SetTag("db.operation.type", "save");
                }
                else if (span.DisplayName.Contains("ExecuteReader") || 
                         span.DisplayName.Contains("Query"))
                {
                    span.SetTag("db.operation", "read");
                    span.SetTag("db.operation.type", "query");
                }
            }
            else if (IsDatabaseActivityType(span, "Npgsql"))
            {
                span.SetTag("db.system", "postgresql");
                span.SetTag("db.technology", "PostgreSQL");
            }

            // Set operation type if available in tags
            if (span.GetTagItem("db.operation") is null)
            {
                if (span.OperationName.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) || 
                    span.DisplayName.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
                {
                    span.SetTag("db.operation", "read");
                }
                else if (span.OperationName.StartsWith("INSERT", StringComparison.OrdinalIgnoreCase) || 
                         span.DisplayName.StartsWith("INSERT", StringComparison.OrdinalIgnoreCase))
                {
                    span.SetTag("db.operation", "write");
                }
                else if (span.OperationName.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase) || 
                         span.DisplayName.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase))
                {
                    span.SetTag("db.operation", "write");
                }
                else if (span.OperationName.StartsWith("DELETE", StringComparison.OrdinalIgnoreCase) || 
                         span.DisplayName.StartsWith("DELETE", StringComparison.OrdinalIgnoreCase))
                {
                    span.SetTag("db.operation", "write");
                }
            }
            
            // Add span kind if not set
            if (span.Kind == ActivityKind.Internal)
            {
                span.SetTag("span.kind", "client");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error enriching database operation span");
        }
    }

    /// <summary>
    /// Adds performance metrics to database spans at end
    /// </summary>
    public void EnrichSpanAtEnd(Activity span)
    {
        if (!IsDatabaseOperation(span)) return;
        
        try
        {
            // Add duration metric explicitly
            var durationMs = (span.Stop - span.Start).TotalMilliseconds;
            span.SetTag("db.operation.duration_ms", durationMs);
            
            // Flag slow database operations
            if (durationMs > 100)
            {
                span.SetTag("db.operation.slow", true);
            }
            
            // Add result information if available
            if (span.GetTagItem("db.result.rows") != null)
            {
                // Already has result information
            }
            else if (span.GetTagItem("rows.affected") is string rowsAffected)
            {
                span.SetTag("db.result.rows", rowsAffected);
            }
            
            // Handle errors
            if (span.Status == ActivityStatusCode.Error)
            {
                span.SetTag("db.operation.success", false);
                
                // Extract and clean error information
                if (span.GetTagItem("error.type") is string errorType)
                {
                    // Already has error type
                }
                else if (span.GetTagItem("exception.type") is string exceptionType)
                {
                    span.SetTag("error.type", exceptionType);
                }
            }
            else
            {
                span.SetTag("db.operation.success", true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error enriching database operation span at end");
        }
    }
    
    /// <summary>
    /// Determines if a span represents a database operation
    /// </summary>
    private bool IsDatabaseOperation(Activity span)
    {
        // Check for known database source names
        var sourceName = span.Source?.Name ?? string.Empty;
        if (sourceName.Contains("System.Data") || 
            sourceName.Contains("Microsoft.EntityFrameworkCore") ||
            sourceName.Contains("Npgsql"))
        {
            return true;
        }
        
        // Check for database-related tags
        if (span.GetTagItem("db.system") != null || 
            span.GetTagItem("db.name") != null ||
            span.GetTagItem("db.statement") != null)
        {
            return true;
        }
        
        // Check operation names
        var displayName = span.DisplayName.ToLowerInvariant();
        return displayName.Contains("sql") || 
               displayName.Contains("query") || 
               displayName.Contains("database") ||
               displayName.Contains("savechanges");
    }
    
    /// <summary>
    /// Checks if a span comes from a specific database provider
    /// </summary>
    private bool IsDatabaseActivityType(Activity span, string providerName)
    {
        var sourceName = span.Source?.Name ?? string.Empty;
        return sourceName.Contains(providerName);
    }
}
