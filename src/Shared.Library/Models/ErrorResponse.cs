namespace Shared.Library.Models;

/// <summary>
/// Standardized error response for API errors
/// </summary>
public class ErrorResponse
{
    /// <summary>
    /// Correlation ID for tracking the request across systems
    /// </summary>
    public string CorrelationId { get; set; } = string.Empty;
    
    /// <summary>
    /// HTTP status code
    /// </summary>
    public int Status { get; set; }
    
    /// <summary>
    /// Error title or category
    /// </summary>
    public string Error { get; set; } = string.Empty;
    
    /// <summary>
    /// User-friendly error message
    /// </summary>
    public string Message { get; set; } = string.Empty;
    
    /// <summary>
    /// When the error occurred
    /// </summary>
    public DateTimeOffset Timestamp { get; set; }
    
    /// <summary>
    /// Additional error details (only included in development)
    /// </summary>
    public string? Details { get; set; }
    
    /// <summary>
    /// Information about inner exceptions (only included in development)
    /// </summary>
    public ErrorDetail? InnerError { get; set; }
}

/// <summary>
/// Details about an inner exception
/// </summary>
public class ErrorDetail
{
    /// <summary>
    /// Exception message
    /// </summary>
    public string Message { get; set; } = string.Empty;
    
    /// <summary>
    /// Exception type name
    /// </summary>
    public string Type { get; set; } = string.Empty;
    
    /// <summary>
    /// Stack trace
    /// </summary>
    public string? StackTrace { get; set; }
}
