using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Shared.Library.Telemetry.Baggage;

/// <summary>
/// Manages OpenTelemetry baggage for propagating business context across service boundaries
/// </summary>
public class BaggageManager
{
    // Standard baggage keys for common business contexts
    public static class Keys
    {
        // Customer-related baggage
        public const string CustomerId = "customer.id";
        public const string CustomerType = "customer.type";
        public const string CustomerTier = "customer.tier";

        // Order-related baggage
        public const string OrderId = "order.id";
        public const string OrderTotal = "order.total";
        public const string OrderPriority = "order.priority";

        // Transaction-related baggage
        public const string TransactionId = "transaction.id";
        public const string CorrelationId = "correlation.id";
        public const string RequestSource = "request.source";

        // Business context
        public const string BusinessUnit = "business.unit";
        public const string Channel = "business.channel";
        public const string Region = "business.region";

        // Feature flags and configuration
        public const string FeatureFlags = "config.features";
        public const string ExperimentId = "config.experiment";

        // Service info
        public const string ServiceName = "service.name";
        public const string ServiceVersion = "service.version";
        public const string ServiceInstance = "service.instance";
    }

    private readonly ILogger<BaggageManager> _logger;
    private readonly IHttpContextAccessor? _httpContextAccessor;

    /// <summary>
    /// Creates a new instance of the BaggageManager
    /// </summary>
    public BaggageManager(ILogger<BaggageManager> logger, IHttpContextAccessor? httpContextAccessor = null)
    {
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
    }

    /// <summary>
    /// Sets a baggage item on the current activity
    /// </summary>
    /// <param name="key">Baggage key</param>
    /// <param name="value">Baggage value</param>
    public void Set(string key, string? value)
    {
        if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(value)) return;

        var activity = Activity.Current;
        if (activity != null)
        {
            activity.AddBaggage(key, value);
            _logger.LogTrace("Set baggage {Key}={Value}", key, value);
        }
        else
        {
            _logger.LogDebug("Could not set baggage {Key} - no current activity", key);
        }
    }

    /// <summary>
    /// Sets multiple baggage items at once
    /// </summary>
    public void SetMany(Dictionary<string, string> baggageItems)
    {
        var activity = Activity.Current;
        if (activity != null)
        {
            foreach (var kvp in baggageItems)
            {
                if (!string.IsNullOrEmpty(kvp.Key) && !string.IsNullOrEmpty(kvp.Value))
                {
                    activity.AddBaggage(kvp.Key, kvp.Value);
                }
            }

            _logger.LogTrace("Set {Count} baggage items", baggageItems.Count);
        }
        else
        {
            _logger.LogDebug("Could not set multiple baggage items - no current activity");
        }
    }

    /// <summary>
    /// Gets a baggage item from the current activity
    /// </summary>
    /// <returns>The baggage value or null if not found</returns>
    public string? Get(string key)
    {
        var activity = Activity.Current;
        return activity?.GetBaggageItem(key);
    }

    /// <summary>
    /// Gets a baggage item, with a default value if not found
    /// </summary>
    public string GetOrDefault(string key, string defaultValue)
    {
        return Get(key) ?? defaultValue;
    }

    /// <summary>
    /// Gets a baggage item and converts it to the specified type
    /// </summary>
    public T? GetAs<T>(string key, Func<string, T>? converter = null)
    {
        var value = Get(key);
        if (string.IsNullOrEmpty(value)) return default;

        try
        {
            if (converter != null)
                return converter(value);

            return (T)Convert.ChangeType(value, typeof(T));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to convert baggage {Key} with value '{Value}' to type {Type}",
                key, value, typeof(T).Name);
            return default;
        }
    }

    /// <summary>
    /// Gets all baggage items from the current activity
    /// </summary>
    public Dictionary<string, string> GetAll()
    {
        var activity = Activity.Current;
        if (activity == null) return new Dictionary<string, string>();

        return activity.Baggage.ToDictionary(x => x.Key, x => x.Value ?? string.Empty);
    }

    /// <summary>
    /// Gets all baggage items with a specific prefix
    /// </summary>
    public Dictionary<string, string> GetAllWithPrefix(string prefix)
    {
        return GetAll()
            .Where(kvp => kvp.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(x => x.Key, x => x.Value);
    }

    /// <summary>
    /// Checks if a baggage item exists and has a specific value
    /// </summary>
    public bool HasValue(string key, string value)
    {
        var baggage = Get(key);
        return baggage != null && baggage.Equals(value, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Sets customer information in baggage
    /// </summary>
    public void SetCustomerContext(string customerId, string? customerType = null, string? customerTier = null)
    {
        Set(Keys.CustomerId, customerId);

        if (!string.IsNullOrEmpty(customerType))
            Set(Keys.CustomerType, customerType);

        if (!string.IsNullOrEmpty(customerTier))
            Set(Keys.CustomerTier, customerTier);
    }

    /// <summary>
    /// Sets order information in baggage
    /// </summary>
    public void SetOrderContext(string orderId, decimal? orderTotal = null, string? priority = null)
    {
        Set(Keys.OrderId, orderId);

        if (orderTotal.HasValue)
            Set(Keys.OrderTotal, orderTotal.Value.ToString("F2"));

        if (!string.IsNullOrEmpty(priority))
            Set(Keys.OrderPriority, priority);
    }

    /// <summary>
    /// Sets transaction information in baggage
    /// </summary>
    public void SetTransactionContext(string? transactionId = null, string? correlationId = null)
    {
        // Generate IDs if not provided
        transactionId ??= Guid.NewGuid().ToString();
        correlationId ??= Get(Keys.CorrelationId) ?? Guid.NewGuid().ToString();

        Set(Keys.TransactionId, transactionId);
        Set(Keys.CorrelationId, correlationId);
    }

    /// <summary>
    /// Gets the current customer ID from baggage
    /// </summary>
    public string? GetCustomerId() => Get(Keys.CustomerId);

    /// <summary>
    /// Gets the current order ID from baggage
    /// </summary>
    public string? GetOrderId() => Get(Keys.OrderId);

    /// <summary>
    /// Gets the transaction ID from baggage, or generates a new one
    /// </summary>
    public string GetTransactionId()
    {
        var transactionId = Get(Keys.TransactionId);
        if (string.IsNullOrEmpty(transactionId))
        {
            transactionId = Guid.NewGuid().ToString();
            Set(Keys.TransactionId, transactionId);
        }
        return transactionId;
    }

    /// <summary>
    /// Gets the correlation ID from baggage, HTTP headers, or generates a new one
    /// </summary>
    public string GetCorrelationId()
    {
        // First try to get from baggage
        var correlationId = Get(Keys.CorrelationId);
        if (!string.IsNullOrEmpty(correlationId))
            return correlationId;

        // Then try to get from HTTP headers
        if (_httpContextAccessor?.HttpContext != null)
        {
            var headers = _httpContextAccessor.HttpContext.Request.Headers;

            // Check common correlation ID header names
            foreach (var headerName in new[] { "X-Correlation-ID", "X-Request-ID", "Request-ID", "Correlation-ID" })
            {
                if (headers.TryGetValue(headerName, out var headerValue) && !string.IsNullOrEmpty(headerValue))
                {
                    correlationId = headerValue;
                    Set(Keys.CorrelationId, correlationId);
                    return correlationId;
                }
            }
        }

        // Generate a new one if not found
        correlationId = Guid.NewGuid().ToString();
        Set(Keys.CorrelationId, correlationId);
        return correlationId;
    }

    /// <summary>
    /// Copies current baggage to tags on the active span for local visibility
    /// </summary>
    public void CopyBaggageToTags()
    {
        var activity = Activity.Current;
        if (activity == null) return;

        foreach (var item in activity.Baggage)
        {
            // Skip empty values
            if (string.IsNullOrEmpty(item.Value)) continue;

            // Add with baggage prefix to distinguish from regular tags
            activity.SetTag($"baggage.{item.Key}", item.Value);
        }
    }

    /// <summary>
    /// Creates a conditional processor that executes code only if a baggage condition is met
    /// </summary>
    public ConditionalProcessor When(string key, string expectedValue)
    {
        return new ConditionalProcessor(key, expectedValue, this);
    }

    /// <summary>
    /// Creates a conditional processor based on customer tier
    /// </summary>
    public ConditionalProcessor WhenCustomerTier(string tier)
    {
        return new ConditionalProcessor(Keys.CustomerTier, tier, this);
    }

    /// <summary>
    /// Creates a conditional processor based on order priority
    /// </summary>
    public ConditionalProcessor WhenOrderPriority(string priority)
    {
        return new ConditionalProcessor(Keys.OrderPriority, priority, this);
    }

    /// <summary>
    /// Helper class for conditional processing based on baggage values
    /// </summary>
    public class ConditionalProcessor
    {
        private readonly string _key;
        private readonly string _expectedValue;
        private readonly BaggageManager _baggageManager;

        internal ConditionalProcessor(string key, string expectedValue, BaggageManager baggageManager)
        {
            _key = key;
            _expectedValue = expectedValue;
            _baggageManager = baggageManager;
        }

        /// <summary>
        /// Executes the specified action if the baggage condition is met
        /// </summary>
        public void Execute(Action action)
        {
            if (_baggageManager.HasValue(_key, _expectedValue))
            {
                action();
            }
        }

        /// <summary>
        /// Executes the specified function if the baggage condition is met, otherwise returns the default value
        /// </summary>
        public T ExecuteOrDefault<T>(Func<T> func, T defaultValue)
        {
            return _baggageManager.HasValue(_key, _expectedValue) ? func() : defaultValue;
        }

        /// <summary>
        /// Executes the specified async function if the baggage condition is met
        /// </summary>
        public async Task ExecuteAsync(Func<Task> asyncAction)
        {
            if (_baggageManager.HasValue(_key, _expectedValue))
            {
                await asyncAction();
            }
        }

        /// <summary>
        /// Executes the specified async function if the baggage condition is met, otherwise returns the default value
        /// </summary>
        public async Task<T> ExecuteOrDefaultAsync<T>(Func<Task<T>> asyncFunc, T defaultValue)
        {
            return _baggageManager.HasValue(_key, _expectedValue) ? await asyncFunc() : defaultValue;
        }
    }
}

/// <summary>
/// Extension methods for registering the BaggageManager
/// </summary>
public static class BaggageManagerExtensions
{
    /// <summary>
    /// Adds the BaggageManager to the service collection
    /// </summary>
    public static IServiceCollection AddBaggageManager(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddSingleton<BaggageManager>();

        return services;
    }
}
