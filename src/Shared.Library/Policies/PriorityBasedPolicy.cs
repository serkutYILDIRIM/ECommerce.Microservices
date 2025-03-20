using Polly;
using Polly.Retry;
using Shared.Library.Telemetry.Baggage;

namespace Shared.Library.Policies;

/// <summary>
/// Provides policies that adjust behavior based on priority information in baggage
/// </summary>
public static class PriorityBasedPolicy
{
    /// <summary>
    /// Creates a retry policy that adjusts retry count and delay based on customer tier and order priority
    /// </summary>
    public static AsyncRetryPolicy<T> CreatePriorityAwareRetryPolicy<T>(
        BaggageManager baggageManager,
        int standardRetryCount = 3,
        int premiumRetryCount = 5)
    {
        return Policy<T>
            .Handle<HttpRequestException>()
            .Or<TimeoutException>()
            .OrTransientHttpError()
            .WaitAndRetryAsync(
                retryCount: GetRetryCount(),
                sleepDurationProvider: GetDelay,
                onRetryAsync: (result, timeSpan, retryCount, context) =>
                {
                    // Log retry with context
                    var logger = context.GetLogger();
                    var operation = context.OperationKey;
                    var customerId = baggageManager.Get(BaggageManager.Keys.CustomerId);
                    var customerTier = baggageManager.Get(BaggageManager.Keys.CustomerTier);
                    var priority = baggageManager.Get(BaggageManager.Keys.OrderPriority);
                    
                    logger?.LogWarning("Retry {RetryCount} for operation {Operation}. " +
                        "Customer: {CustomerId}, Tier: {CustomerTier}, Priority: {Priority}. " +
                        "Waiting {DelayMs}ms before next retry.",
                        retryCount, operation, customerId, customerTier, priority, timeSpan.TotalMilliseconds);
                    
                    return Task.CompletedTask;
                });
        
        // Determine retry count based on baggage
        int GetRetryCount()
        {
            var isPremiumCustomer = IsPremiumCustomer();
            var isHighPriority = IsHighPriority();
            
            // Premium customers or high priority orders get more retries
            if (isPremiumCustomer || isHighPriority)
            {
                return premiumRetryCount;
            }
            
            return standardRetryCount;
        }
        
        // Calculate delay based on priority and retry count
        TimeSpan GetDelay(int retryAttempt, Context context)
        {
            var isPremiumCustomer = IsPremiumCustomer();
            var isHighPriority = IsHighPriority();
            
            // Base delay calculation
            var baseDelay = retryAttempt * 200; // 200ms, 400ms, 600ms, etc.
            
            // Priority customers get shorter delays
            if (isPremiumCustomer || isHighPriority)
            {
                baseDelay /= 2; // 100ms, 200ms, 300ms, etc.
            }
            
            // Add some jitter to avoid the "thundering herd" problem
            var jitter = new Random().Next(0, 100);
            
            return TimeSpan.FromMilliseconds(baseDelay + jitter);
        }
        
        // Helper methods to check business context
        bool IsPremiumCustomer()
        {
            var customerTier = baggageManager.Get(BaggageManager.Keys.CustomerTier);
            return !string.IsNullOrEmpty(customerTier) && 
                (customerTier.Equals("premium", StringComparison.OrdinalIgnoreCase) || 
                 customerTier.Equals("gold", StringComparison.OrdinalIgnoreCase));
        }
        
        bool IsHighPriority()
        {
            var priority = baggageManager.Get(BaggageManager.Keys.OrderPriority);
            return !string.IsNullOrEmpty(priority) &&
                (priority.Equals("high", StringComparison.OrdinalIgnoreCase) || 
                 priority.Equals("urgent", StringComparison.OrdinalIgnoreCase));
        }
    }
    
    /// <summary>
    /// Creates a timeout policy that adjusts timeout based on customer tier and order priority
    /// </summary>
    public static IAsyncPolicy<T> CreatePriorityAwareTimeoutPolicy<T>(
        BaggageManager baggageManager,
        TimeSpan standardTimeout = default,
        TimeSpan premiumTimeout = default)
    {
        if (standardTimeout == default)
            standardTimeout = TimeSpan.FromSeconds(10);
            
        if (premiumTimeout == default)
            premiumTimeout = TimeSpan.FromSeconds(30);
            
        return Policy.TimeoutAsync<T>(GetTimeoutPeriod);
        
        TimeSpan GetTimeoutPeriod(Context context)
        {
            var customerTier = baggageManager.Get(BaggageManager.Keys.CustomerTier);
            var priority = baggageManager.Get(BaggageManager.Keys.OrderPriority);
            
            bool isPremiumCustomer = !string.IsNullOrEmpty(customerTier) && 
                (customerTier.Equals("premium", StringComparison.OrdinalIgnoreCase) || 
                 customerTier.Equals("gold", StringComparison.OrdinalIgnoreCase));
                 
            bool isHighPriority = !string.IsNullOrEmpty(priority) &&
                (priority.Equals("high", StringComparison.OrdinalIgnoreCase) || 
                 priority.Equals("urgent", StringComparison.OrdinalIgnoreCase));
                 
            return (isPremiumCustomer || isHighPriority) ? premiumTimeout : standardTimeout;
        }
    }
}

/// <summary>
/// Extension methods for Polly Context
/// </summary>
public static class PollyContextExtensions
{
    /// <summary>
    /// Gets the logger from the Polly context if available
    /// </summary>
    public static ILogger GetLogger(this Context context)
    {
        if (context.TryGetValue("logger", out var loggerObj) && loggerObj is ILogger logger)
        {
            return logger;
        }
        
        return null;
    }
}
