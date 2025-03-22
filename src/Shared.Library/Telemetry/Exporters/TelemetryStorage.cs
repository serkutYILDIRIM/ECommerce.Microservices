using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Shared.Library.Telemetry.Exporters;

/// <summary>
/// In-memory storage for telemetry data that enables testing and analysis
/// </summary>
public class TelemetryStorage
{
    private readonly ConcurrentQueue<SpanRecord> _recentSpans = new();
    private readonly ConcurrentQueue<SpanRecord> _errorSpans = new();
    private readonly ConcurrentQueue<SpanRecord> _slowSpans = new();
    private readonly int _maxStoredSpans;
    private readonly ILogger<TelemetryStorage> _logger;
    
    public TelemetryStorage(ILogger<TelemetryStorage> logger, int maxStoredSpans = 1000)
    {
        _maxStoredSpans = maxStoredSpans;
        _logger = logger;
        _logger.LogInformation("Telemetry storage initialized with capacity for {MaxSpans} spans", maxStoredSpans);
    }
    
    /// <summary>
    /// Add a span to the recent spans queue
    /// </summary>
    public void AddSpan(SpanRecord span)
    {
        _recentSpans.Enqueue(span);
        TrimQueue(_recentSpans);
    }
    
    /// <summary>
    /// Add a span to the error spans queue
    /// </summary>
    public void AddErrorSpan(SpanRecord span)
    {
        _errorSpans.Enqueue(span);
        TrimQueue(_errorSpans);
    }
    
    /// <summary>
    /// Add a span to the slow spans queue
    /// </summary>
    public void AddSlowSpan(SpanRecord span)
    {
        _slowSpans.Enqueue(span);
        TrimQueue(_slowSpans);
    }
    
    /// <summary>
    /// Get recent spans with optional filtering
    /// </summary>
    public IEnumerable<SpanRecord> GetRecentSpans(int count = 100, string? serviceName = null)
    {
        var spans = _recentSpans.ToArray();
        
        if (!string.IsNullOrEmpty(serviceName))
        {
            spans = spans.Where(s => s.ServiceName == serviceName).ToArray();
        }
        
        return spans.Reverse().Take(count);
    }
    
    /// <summary>
    /// Get error spans with optional filtering
    /// </summary>
    public IEnumerable<SpanRecord> GetErrorSpans(int count = 100, string? serviceName = null)
    {
        var spans = _errorSpans.ToArray();
        
        if (!string.IsNullOrEmpty(serviceName))
        {
            spans = spans.Where(s => s.ServiceName == serviceName).ToArray();
        }
        
        return spans.Reverse().Take(count);
    }
    
    /// <summary>
    /// Get slow spans with optional filtering
    /// </summary>
    public IEnumerable<SpanRecord> GetSlowSpans(int count = 100, string? serviceName = null)
    {
        var spans = _slowSpans.ToArray();
        
        if (!string.IsNullOrEmpty(serviceName))
        {
            spans = spans.Where(s => s.ServiceName == serviceName).ToArray();
        }
        
        return spans.Reverse().Take(count);
    }
    
    /// <summary>
    /// Get a specific trace by ID
    /// </summary>
    public IEnumerable<SpanRecord> GetTraceById(string traceId)
    {
        return _recentSpans
            .Where(s => s.TraceId == traceId)
            .OrderBy(s => s.StartTime);
    }
    
    /// <summary>
    /// Clear all stored spans
    /// </summary>
    public void Clear()
    {
        while (_recentSpans.TryDequeue(out _)) { }
        while (_errorSpans.TryDequeue(out _)) { }
        while (_slowSpans.TryDequeue(out _)) { }
        
        _logger.LogInformation("Telemetry storage cleared");
    }
    
    /// <summary>
    /// Get statistics about stored spans
    /// </summary>
    public TelemetryStorageStats GetStats()
    {
        return new TelemetryStorageStats
        {
            TotalSpans = _recentSpans.Count,
            ErrorSpans = _errorSpans.Count,
            SlowSpans = _slowSpans.Count,
            UniqueTraces = _recentSpans.Select(s => s.TraceId).Distinct().Count(),
            UniqueServices = _recentSpans.Select(s => s.ServiceName).Distinct().Count()
        };
    }
    
    // Keep the queue size under the maximum limit
    private void TrimQueue<T>(ConcurrentQueue<T> queue)
    {
        while (queue.Count > _maxStoredSpans && queue.TryDequeue(out _)) { }
    }
}

/// <summary>
/// Statistics about the telemetry storage
/// </summary>
public class TelemetryStorageStats
{
    public int TotalSpans { get; set; }
    public int ErrorSpans { get; set; }
    public int SlowSpans { get; set; }
    public int UniqueTraces { get; set; }
    public int UniqueServices { get; set; }
}
