using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.Extensions.Http;
using Shared.Library.Clients;
using Shared.Library.Clients.Implementation;
using Shared.Library.Telemetry;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Shared.Library.Telemetry.Baggage;

namespace Shared.Library.Extensions;

public static class HttpClientExtensions
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static IServiceCollection AddMicroserviceClients(
        this IServiceCollection services,
        string serviceName,
        string productCatalogBaseUrl = "https://localhost:7001",
        string orderProcessingBaseUrl = "https://localhost:7002",
        string inventoryManagementBaseUrl = "https://localhost:7003")
    {
        // Add the HTTP client context propagator
        services.AddSingleton(new HttpClientContextPropagator(serviceName));

        // Add resilience policies
        var retryPolicy = HttpPolicyExtensions
            .HandleTransientHttpError()
            .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));

        var circuitBreakerPolicy = HttpPolicyExtensions
            .HandleTransientHttpError()
            .CircuitBreakerAsync(5, TimeSpan.FromSeconds(30));

        // Register Product Catalog Client
        services.AddHttpClient<IProductCatalogClient, ProductCatalogClient>(client =>
        {
            client.BaseAddress = new Uri(productCatalogBaseUrl);
        })
        .AddHttpMessageHandler(provider => 
            new TracingMessageHandler(provider.GetRequiredService<HttpClientContextPropagator>()))
        .AddPolicyHandler(retryPolicy)
        .AddPolicyHandler(circuitBreakerPolicy);

        // Register Order Processing Client
        services.AddHttpClient<IOrderProcessingClient, OrderProcessingClient>(client =>
        {
            client.BaseAddress = new Uri(orderProcessingBaseUrl);
        })
        .AddHttpMessageHandler(provider => 
            new TracingMessageHandler(provider.GetRequiredService<HttpClientContextPropagator>()))
        .AddPolicyHandler(retryPolicy)
        .AddPolicyHandler(circuitBreakerPolicy);

        // Register Inventory Management Client
        services.AddHttpClient<IInventoryManagementClient, InventoryManagementClient>(client =>
        {
            client.BaseAddress = new Uri(inventoryManagementBaseUrl);
        })
        .AddHttpMessageHandler(provider => 
            new TracingMessageHandler(provider.GetRequiredService<HttpClientContextPropagator>()))
        .AddPolicyHandler(retryPolicy)
        .AddPolicyHandler(circuitBreakerPolicy);

        return services;
    }

    /// <summary>
    /// Sends a GET request to the specified URI with automatic baggage propagation
    /// </summary>
    public static async Task<T?> GetFromJsonAsync<T>(
        this HttpClient client,
        string requestUri,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        PropagateContext(request);
        
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(stream, _jsonOptions, cancellationToken);
    }

    /// <summary>
    /// Sends a POST request to the specified URI with automatic baggage propagation
    /// </summary>
    public static async Task<TResponse?> PostAsJsonAsync<TRequest, TResponse>(
        this HttpClient client,
        string requestUri,
        TRequest value,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
        PropagateContext(request);
        
        if (value != null)
        {
            var json = JsonSerializer.Serialize(value, _jsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }
        
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<TResponse>(stream, _jsonOptions, cancellationToken);
    }

    /// <summary>
    /// Sends a POST request to the specified URI with automatic baggage propagation
    /// </summary>
    public static async Task<HttpResponseMessage> PostAsJsonAsync<TRequest>(
        this HttpClient client,
        string requestUri,
        TRequest value,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
        PropagateContext(request);
        
        if (value != null)
        {
            var json = JsonSerializer.Serialize(value, _jsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }
        
        return await client.SendAsync(request, cancellationToken);
    }

    /// <summary>
    /// Sends a PUT request to the specified URI with automatic baggage propagation
    /// </summary>
    public static async Task<TResponse?> PutAsJsonAsync<TRequest, TResponse>(
        this HttpClient client,
        string requestUri,
        TRequest value,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, requestUri);
        PropagateContext(request);
        
        if (value != null)
        {
            var json = JsonSerializer.Serialize(value, _jsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }
        
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<TResponse>(stream, _jsonOptions, cancellationToken);
    }

    /// <summary>
    /// Creates a new HttpRequestMessage with the current context properly propagated
    /// </summary>
    public static HttpRequestMessage CreateRequestWithContext(
        this HttpClient client,
        HttpMethod method,
        string requestUri)
    {
        var request = new HttpRequestMessage(method, requestUri);
        PropagateContext(request);
        return request;
    }

    /// <summary>
    /// Propagates the current activity and baggage to the HttpRequestMessage
    /// </summary>
    public static void PropagateContext(HttpRequestMessage request)
    {
        // Get the current activity
        var activity = Activity.Current;
        if (activity == null) return;
        
        // Add trace context (W3C traceparent)
        var traceparent = $"00-{activity.TraceId}-{activity.SpanId}-{(activity.Recorded ? "01" : "00")}";
        request.Headers.Add("traceparent", traceparent);
        
        if (!string.IsNullOrEmpty(activity.TraceStateString))
        {
            request.Headers.Add("tracestate", activity.TraceStateString);
        }
        
        // Propagate baggage in W3C format
        if (activity.Baggage.Any())
        {
            var baggageValues = new List<string>();
            
            foreach (var baggageItem in activity.Baggage)
            {
                if (!string.IsNullOrEmpty(baggageItem.Value))
                {
                    baggageValues.Add($"{baggageItem.Key}={Uri.EscapeDataString(baggageItem.Value)}");
                }
            }
            
            if (baggageValues.Any())
            {
                request.Headers.Add("baggage", string.Join(",", baggageValues));
            }
        }
        
        // Add correlation ID as a specific header for systems that don't support W3C trace context
        var correlationId = activity.GetBaggageItem(BaggageManager.Keys.CorrelationId);
        if (!string.IsNullOrEmpty(correlationId))
        {
            request.Headers.Add("X-Correlation-ID", correlationId);
        }
        
        // Propagate important business context as dedicated headers
        PropagateBusinessContextToHeaders(request.Headers, activity);
    }
    
    /// <summary>
    /// Propagates important business context to HTTP headers
    /// </summary>
    private static void PropagateBusinessContextToHeaders(HttpRequestHeaders headers, Activity activity)
    {
        // Add common business context items as headers
        var customerId = activity.GetBaggageItem(BaggageManager.Keys.CustomerId);
        if (!string.IsNullOrEmpty(customerId))
        {
            headers.Add("X-Customer-ID", customerId);
        }
        
        var customerTier = activity.GetBaggageItem(BaggageManager.Keys.CustomerTier);
        if (!string.IsNullOrEmpty(customerTier))
        {
            headers.Add("X-Customer-Tier", customerTier);
        }
        
        var orderId = activity.GetBaggageItem(BaggageManager.Keys.OrderId);
        if (!string.IsNullOrEmpty(orderId))
        {
            headers.Add("X-Order-ID", orderId);
        }
        
        var orderPriority = activity.GetBaggageItem(BaggageManager.Keys.OrderPriority);
        if (!string.IsNullOrEmpty(orderPriority))
        {
            headers.Add("X-Order-Priority", orderPriority);
        }
        
        var transactionId = activity.GetBaggageItem(BaggageManager.Keys.TransactionId);
        if (!string.IsNullOrEmpty(transactionId))
        {
            headers.Add("X-Transaction-ID", transactionId);
        }
        
        // Add source service for debugging
        var sourceName = activity.GetBaggageItem(BaggageManager.Keys.ServiceName);
        if (!string.IsNullOrEmpty(sourceName))
        {
            headers.Add("X-Source-Service", sourceName);
        }
    }
}

/// <summary>
/// Delegating handler that ensures trace context is propagated in HTTP requests
/// </summary>
public class TracingMessageHandler : DelegatingHandler
{
    private readonly HttpClientContextPropagator _propagator;

    public TracingMessageHandler(HttpClientContextPropagator propagator)
    {
        _propagator = propagator;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _propagator.EnrichRequest(request, Activity.Current);
        return base.SendAsync(request, cancellationToken);
    }
}
