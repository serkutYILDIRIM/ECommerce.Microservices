using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Shared.Library.Telemetry.Processors;

/// <summary>
/// Enriches spans with HTTP context information from IHttpContextAccessor
/// </summary>
public class HttpContextEnricher : ISpanEnricher
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<HttpContextEnricher> _logger;

    public HttpContextEnricher(IHttpContextAccessor httpContextAccessor, ILogger<HttpContextEnricher> logger)
    {
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    /// <summary>
    /// Adds HTTP request information to spans at start time
    /// </summary>
    public void EnrichSpanAtStart(Activity span)
    {
        var context = _httpContextAccessor.HttpContext;
        if (context == null) return;

        try
        {
            var request = context.Request;

            // Add standard HTTP attributes
            span.SetTag("http.method", request.Method);
            span.SetTag("http.scheme", request.Scheme);
            span.SetTag("http.host", request.Host.Value);
            span.SetTag("http.target", request.Path.Value);
            span.SetTag("http.flavor", GetHttpProtocol(request.Protocol));
            span.SetTag("http.user_agent", request.Headers["User-Agent"].ToString());

            // Add custom headers (with filtering for sensitive data)
            if (request.Headers.TryGetValue("X-Correlation-ID", out var correlationId))
            {
                span.SetTag("http.correlation_id", correlationId.ToString());
                span.AddBaggage("correlation.id", correlationId.ToString());
            }

            if (request.Headers.TryGetValue("X-Source-Service", out var sourceService))
            {
                span.SetTag("http.source_service", sourceService.ToString());
            }

            // Add client information
            span.SetTag("http.client_ip", GetClientIp(context));

            // Add user information if authenticated
            if (context.User?.Identity?.IsAuthenticated == true)
            {
                span.SetTag("enduser.id", context.User.Identity.Name);
                span.SetTag("enduser.authenticated", true);

                // Add roles if available
                var roles = context.User.Claims.Where(c => c.Type == "role").Select(c => c.Value);
                if (roles.Any())
                {
                    span.SetTag("enduser.roles", string.Join(",", roles));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error enriching span with HTTP context");
        }
    }

    /// <summary>
    /// Adds HTTP response information to spans at end time
    /// </summary>
    public void EnrichSpanAtEnd(Activity span)
    {
        var context = _httpContextAccessor.HttpContext;
        if (context == null) return;

        try
        {
            var response = context.Response;

            // Add response status
            span.SetTag("http.status_code", response.StatusCode);

            // Determine status code category and set appropriate span status
            if (response.StatusCode >= 400)
            {
                var errorType = response.StatusCode >= 500 ? "server" : "client";
                span.SetTag("error", true);
                span.SetTag("error.type", $"http.{errorType}_error");

                // Set span status based on HTTP status
                span.SetStatus(response.StatusCode >= 500 ?
                    ActivityStatusCode.Error :
                    ActivityStatusCode.Unset, $"HTTP {response.StatusCode}");
            }

            // Add response size if available and content length is set
            if (response.ContentLength.HasValue)
            {
                span.SetTag("http.response_content_length", response.ContentLength.Value);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error enriching span with HTTP response context");
        }
    }

    /// <summary>
    /// Gets the HTTP protocol version
    /// </summary>
    private string GetHttpProtocol(string protocol)
    {
        return protocol switch
        {
            "HTTP/1.0" => "1.0",
            "HTTP/1.1" => "1.1",
            "HTTP/2" => "2.0",
            "HTTP/3" => "3.0",
            _ => protocol
        };
    }

    /// <summary>
    /// Gets the client IP address with proper forwarding header support
    /// </summary>
    private string GetClientIp(HttpContext context)
    {
        // Check for forwarded headers
        if (context.Request.Headers.TryGetValue("X-Forwarded-For", out var forwardedFor))
        {
            var ips = forwardedFor.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (ips.Length > 0)
                return ips[0].Trim();
        }

        // Fall back to connection remote IP
        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
