using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shared.Library.Telemetry.Baggage;

namespace Shared.Library.Services;

/// <summary>
/// Service that enriches business entities with context from baggage
/// </summary>
public class BusinessContextEnricher
{
    private readonly BaggageManager _baggageManager;
    private readonly ILogger<BusinessContextEnricher> _logger;

    public BusinessContextEnricher(BaggageManager baggageManager, ILogger<BusinessContextEnricher> logger)
    {
        _baggageManager = baggageManager;
        _logger = logger;
    }

    /// <summary>
    /// Extracts business context from baggage to include in operations
    /// </summary>
    public BusinessContext GetBusinessContext()
    {
        var context = new BusinessContext
        {
            CorrelationId = _baggageManager.GetCorrelationId(),
            TransactionId = _baggageManager.GetTransactionId(),
            CustomerId = _baggageManager.Get(BaggageManager.Keys.CustomerId),
            CustomerTier = _baggageManager.Get(BaggageManager.Keys.CustomerTier),
            OrderId = _baggageManager.Get(BaggageManager.Keys.OrderId),
            OrderPriority = _baggageManager.Get(BaggageManager.Keys.OrderPriority),
            Channel = _baggageManager.Get(BaggageManager.Keys.Channel),
            Region = _baggageManager.Get(BaggageManager.Keys.Region),
            RequestSource = _baggageManager.Get(BaggageManager.Keys.RequestSource),
            ServiceName = _baggageManager.Get(BaggageManager.Keys.ServiceName),
            Timestamp = DateTime.UtcNow
        };
        
        return context;
    }
    
    /// <summary>
    /// Enriches an entity with context from baggage
    /// </summary>
    public T EnrichEntity<T>(T entity) where T : class
    {
        if (entity == null) return entity;
        
        // Get common property types that might be on entities
        var entityType = entity.GetType();
        
        // Try to set CorrelationId if the property exists
        var correlationIdProperty = entityType.GetProperty("CorrelationId");
        if (correlationIdProperty?.PropertyType == typeof(string) && 
            correlationIdProperty.CanWrite)
        {
            correlationIdProperty.SetValue(entity, _baggageManager.GetCorrelationId());
        }
        
        // Similarly for other common properties
        TrySetPropertyFromBaggage(entity, "CustomerId", BaggageManager.Keys.CustomerId);
        TrySetPropertyFromBaggage(entity, "OrderId", BaggageManager.Keys.OrderId);
        TrySetPropertyFromBaggage(entity, "TransactionId", BaggageManager.Keys.TransactionId);
        TrySetPropertyFromBaggage(entity, "Channel", BaggageManager.Keys.Channel);
        TrySetPropertyFromBaggage(entity, "CreatedBy", BaggageManager.Keys.ServiceName);
        
        // Set timestamps if available
        var createdAtProperty = entityType.GetProperty("CreatedAt");
        if (createdAtProperty?.PropertyType == typeof(DateTime) && 
            createdAtProperty.CanWrite)
        {
            if ((DateTime)createdAtProperty.GetValue(entity) == default)
            {
                createdAtProperty.SetValue(entity, DateTime.UtcNow);
            }
        }
        
        return entity;
    }
    
    /// <summary>
    /// Tries to set a property on an entity from baggage
    /// </summary>
    private void TrySetPropertyFromBaggage<T>(T entity, string propertyName, string baggageKey) where T : class
    {
        var property = entity.GetType().GetProperty(propertyName);
        if (property?.PropertyType == typeof(string) && property.CanWrite)
        {
            var currentValue = (string)property.GetValue(entity);
            if (string.IsNullOrEmpty(currentValue))
            {
                var baggageValue = _baggageManager.Get(baggageKey);
                if (!string.IsNullOrEmpty(baggageValue))
                {
                    property.SetValue(entity, baggageValue);
                }
            }
        }
    }
    
    /// <summary>
    /// Enriches a request with context from baggage
    /// </summary>
    public TRequest EnrichRequest<TRequest>(TRequest request) where TRequest : class
    {
        return EnrichEntity(request);
    }
    
    /// <summary>
    /// Sets business properties as baggage for outgoing operations
    /// </summary>
    public void SetBusinessContextAsBaggage<T>(T entity) where T : class
    {
        if (entity == null) return;
        
        var entityType = entity.GetType();
        
        // Check common business properties
        CheckAndSetBaggage(entity, "CustomerId", BaggageManager.Keys.CustomerId);
        CheckAndSetBaggage(entity, "OrderId", BaggageManager.Keys.OrderId);
        CheckAndSetBaggage(entity, "TransactionId", BaggageManager.Keys.TransactionId);
        CheckAndSetBaggage(entity, "CustomerTier", BaggageManager.Keys.CustomerTier);
        CheckAndSetBaggage(entity, "OrderPriority", BaggageManager.Keys.OrderPriority);
        CheckAndSetBaggage(entity, "Channel", BaggageManager.Keys.Channel);
        CheckAndSetBaggage(entity, "Region", BaggageManager.Keys.Region);
    }
    
    /// <summary>
    /// Checks if an entity has a property and sets its value as baggage
    /// </summary>
    private void CheckAndSetBaggage<T>(T entity, string propertyName, string baggageKey) where T : class
    {
        var property = entity.GetType().GetProperty(propertyName);
        if (property?.PropertyType == typeof(string) && property.CanRead)
        {
            var value = (string)property.GetValue(entity);
            if (!string.IsNullOrEmpty(value))
            {
                _baggageManager.Set(baggageKey, value);
            }
        }
    }
}

/// <summary>
/// A class capturing the business context from baggage
/// </summary>
public class BusinessContext
{
    public string CorrelationId { get; set; }
    public string TransactionId { get; set; }
    public string CustomerId { get; set; }
    public string CustomerTier { get; set; }
    public string OrderId { get; set; }
    public string OrderPriority { get; set; }
    public string Channel { get; set; }
    public string Region { get; set; }
    public string RequestSource { get; set; }
    public string ServiceName { get; set; }
    public DateTime Timestamp { get; set; }
    
    /// <summary>
    /// Determines if this request is from a premium customer
    /// </summary>
    public bool IsPremiumCustomer => 
        !string.IsNullOrEmpty(CustomerTier) && 
        (CustomerTier.Equals("premium", StringComparison.OrdinalIgnoreCase) || 
         CustomerTier.Equals("gold", StringComparison.OrdinalIgnoreCase));
    
    /// <summary>
    /// Determines if this is a high priority operation
    /// </summary>
    public bool IsHighPriority =>
        !string.IsNullOrEmpty(OrderPriority) && 
        (OrderPriority.Equals("high", StringComparison.OrdinalIgnoreCase) ||
         OrderPriority.Equals("urgent", StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Extension methods for the BusinessContextEnricher
/// </summary>
public static class BusinessContextEnricherExtensions
{
    /// <summary>
    /// Adds the BusinessContextEnricher to the service collection
    /// </summary>
    public static IServiceCollection AddBusinessContextEnricher(this IServiceCollection services)
    {
        services.AddScoped<BaggageManager>();
        services.AddScoped<BusinessContextEnricher>();

        return services;
    }
}
