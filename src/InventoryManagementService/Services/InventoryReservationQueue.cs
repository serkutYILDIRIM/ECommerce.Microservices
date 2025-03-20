using InventoryManagementService.Controllers;
using InventoryManagementService.Data;
using Shared.Library.Services;
using Shared.Library.Telemetry.Baggage;
using Shared.Library.Telemetry.Contexts;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace InventoryManagementService.Services;

/// <summary>
/// A priority queue for inventory reservation requests that uses baggage to determine priority
/// </summary>
public class InventoryReservationQueue : IDisposable
{
    private readonly ConcurrentDictionary<int, ConcurrentQueue<InventoryReservationTask>> _priorityQueues;
    private readonly SemaphoreSlim _processingSemaphore;
    private readonly InventoryDbContext _dbContext;
    private readonly IInventoryBusinessRules _businessRules;
    private readonly ILogger<InventoryReservationQueue> _logger;
    private readonly BaggageManager _baggageManager;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private Task _processingTask;
    
    // Metrics for queue performance
    private readonly ConcurrentDictionary<int, int> _queueSizes = new();
    private readonly ConcurrentDictionary<int, TimeSpan> _averageWaitTimes = new();

    public InventoryReservationQueue(
        InventoryDbContext dbContext,
        IInventoryBusinessRules businessRules,
        ILogger<InventoryReservationQueue> logger,
        BaggageManager baggageManager)
    {
        _dbContext = dbContext;
        _businessRules = businessRules;
        _logger = logger;
        _baggageManager = baggageManager;
        
        // Initialize priority queues (0-10, where 10 is highest priority)
        _priorityQueues = new ConcurrentDictionary<int, ConcurrentQueue<InventoryReservationTask>>();
        for (int i = 0; i <= 10; i++)
        {
            _priorityQueues[i] = new ConcurrentQueue<InventoryReservationTask>();
            _queueSizes[i] = 0;
            _averageWaitTimes[i] = TimeSpan.FromSeconds(i == 10 ? 1 : (11 - i) * 5); // Initial estimate
        }
        
        _processingSemaphore = new SemaphoreSlim(1, 1);
        _cancellationTokenSource = new CancellationTokenSource();
        
        // Start background processing
        _processingTask = Task.Run(() => ProcessQueueAsync(_cancellationTokenSource.Token));
    }

    /// <summary>
    /// Enqueues a reservation task with the specified priority
    /// </summary>
    public Task EnqueueReservation(InventoryReservationTask task)
    {
        // Ensure priority is within bounds
        task.Priority = Math.Clamp(task.Priority, 0, 10);
        
        // Add to the appropriate priority queue
        _priorityQueues[task.Priority].Enqueue(task);
        
        // Update metrics
        _queueSizes[task.Priority] = _priorityQueues[task.Priority].Count;
        
        _logger.LogInformation(
            "Enqueued inventory reservation request for product {ProductId}, priority {Priority}, queue size {QueueSize}",
            task.Request.ProductId, task.Priority, _queueSizes[task.Priority]);
            
        return Task.CompletedTask;
    }
    
    /// <summary>
    /// Gets estimated processing time based on priority and current queue load
    /// </summary>
    public TimeSpan GetEstimatedProcessingTime(int priority)
    {
        // Calculate based on queue size and historic processing times
        int queueSize = _queueSizes[priority];
        TimeSpan averageWaitTime = _averageWaitTimes[priority];
        
        // For empty queue, return the base wait time
        if (queueSize == 0)
            return averageWaitTime;
            
        // Calculate estimated wait based on queue size
        return TimeSpan.FromTicks(averageWaitTime.Ticks * queueSize);
    }
    
    /// <summary>
    /// Processes the queue continuously in background
    /// </summary>
    private async Task ProcessQueueAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await _processingSemaphore.WaitAsync(cancellationToken);
                
                try
                {
                    // Find the highest priority non-empty queue
                    for (int priority = 10; priority >= 0; priority--)
                    {
                        if (_priorityQueues[priority].TryDequeue(out var task))
                        {
                            // Update metrics
                            _queueSizes[priority] = _priorityQueues[priority].Count;
                            
                            // Process the task with its original context
                            await ProcessReservationTaskAsync(task);
                            
                            // Found and processed a task, break to start from highest priority again
                            break;
                        }
                    }
                }
                finally
                {
                    _processingSemaphore.Release();
                }
                
                // Small delay before checking queues again
                await Task.Delay(100, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Normal cancellation, exit
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing reservation queue");
                
                // Brief delay to avoid tight error loops
                await Task.Delay(1000, cancellationToken);
            }
        }
    }
    
    /// <summary>
    /// Processes a specific reservation task with its original context
    /// </summary>
    private async Task ProcessReservationTaskAsync(InventoryReservationTask task)
    {
        // Start timer for metrics
        var stopwatch = Stopwatch.StartNew();
        
        // Use AsyncContext to run the task with the original business context
        var asyncContext = CreateAsyncContextFromTask(task);
        
        try
        {
            await AsyncContext.RunAsync(async () =>
            {
                // Set the baggage from the stored business context
                RestoreBaggageFromBusinessContext(task.BusinessContext);
                
                // Actual processing with original context
                await ProcessReservationAsync(task);
            }, asyncContext);
            
            // Update wait time metrics for this priority
            stopwatch.Stop();
            UpdateAverageWaitTime(task.Priority, stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, 
                "Error processing queued reservation for product {ProductId}, priority {Priority}", 
                task.Request.ProductId, task.Priority);
                
            // Mark as failed
            task.Status = "Failed";
            task.ErrorMessage = ex.Message;
        }
    }
    
    /// <summary>
    /// The actual processing of a reservation once it's dequeued
    /// </summary>
    private async Task ProcessReservationAsync(InventoryReservationTask task)
    {
        // Start span for this operation
        using var activity = new ActivitySource("InventoryReservationQueue")
            .StartActivity("ProcessQueuedReservation");
            
        activity?.SetTag("product.id", task.Request.ProductId);
        activity?.SetTag("order.id", task.Request.OrderId);
        activity?.SetTag("priority", task.Priority);
        activity?.SetTag("queued.time", (DateTime.UtcNow - task.SubmittedAt).TotalMilliseconds);
        
        // Find the inventory item
        var inventoryItem = await _dbContext.InventoryItems.FindAsync(task.Request.ProductId);
        if (inventoryItem == null)
        {
            task.Status = "Failed";
            task.ErrorMessage = $"Product with ID {task.Request.ProductId} not found";
            activity?.SetStatus(ActivityStatusCode.Error, task.ErrorMessage);
            return;
        }
        
        // Evaluate the reservation with business context
        var decision = _businessRules.EvaluateReservation(inventoryItem, task.Request, task.BusinessContext);
        if (!decision.IsAllowed)
        {
            task.Status = "Rejected";
            task.ErrorMessage = decision.Reason;
            activity?.SetStatus(ActivityStatusCode.Error, task.ErrorMessage);
            return;
        }
        
        try
        {
            // Apply the reservation
            inventoryItem.QuantityAvailable -= task.Request.Quantity;
            inventoryItem.ReservedQuantity += task.Request.Quantity;
            inventoryItem.LastUpdated = DateTime.UtcNow;
            
            // Create reservation record
            var reservation = new InventoryReservation
            {
                ProductId = task.Request.ProductId,
                OrderId = task.Request.OrderId,
                Quantity = task.Request.Quantity,
                ReservationDate = DateTime.UtcNow,
                Status = "Active",
                Priority = task.BusinessContext.IsHighPriority,
                CorrelationId = task.BusinessContext.CorrelationId,
                CustomerTier = task.BusinessContext.CustomerTier,
                Notes = $"Processed from queue. Priority: {task.Priority}, Wait time: {(DateTime.UtcNow - task.SubmittedAt).TotalSeconds:F1}s"
            };
            
            // Apply business rules (like extended expiry for premium customers)
            await _businessRules.ApplyBusinessRules(reservation, task.BusinessContext);
            
            // Save the reservation
            _dbContext.InventoryReservations.Add(reservation);
            await _dbContext.SaveChangesAsync();
            
            // Update task status
            task.Status = "Completed";
            task.CompletedAt = DateTime.UtcNow;
            task.ReservationId = reservation.Id;
            
            _logger.LogInformation(
                "Successfully processed queued reservation for product {ProductId}, queue wait time {WaitTime}ms",
                task.Request.ProductId, (task.CompletedAt - task.SubmittedAt).TotalMilliseconds);
        }
        catch (Exception ex)
        {
            task.Status = "Failed";
            task.ErrorMessage = $"Error processing reservation: {ex.Message}";
            activity?.SetStatus(ActivityStatusCode.Error, task.ErrorMessage);
            throw;
        }
    }
    
    /// <summary>
    /// Creates an async context from the task's business context
    /// </summary>
    private AsyncContextScope CreateAsyncContextFromTask(InventoryReservationTask task)
    {
        // We'll create a scope that can be used to run the task with its original context
        return AsyncContext.Capture();
    }
    
    /// <summary>
    /// Restores baggage from the saved business context
    /// </summary>
    private void RestoreBaggageFromBusinessContext(BusinessContext context)
    {
        if (!string.IsNullOrEmpty(context.CorrelationId))
            _baggageManager.Set(BaggageManager.Keys.CorrelationId, context.CorrelationId);
            
        if (!string.IsNullOrEmpty(context.CustomerId))
            _baggageManager.Set(BaggageManager.Keys.CustomerId, context.CustomerId);
            
        if (!string.IsNullOrEmpty(context.CustomerTier))
            _baggageManager.Set(BaggageManager.Keys.CustomerTier, context.CustomerTier);
            
        if (!string.IsNullOrEmpty(context.OrderId))
            _baggageManager.Set(BaggageManager.Keys.OrderId, context.OrderId);
            
        if (!string.IsNullOrEmpty(context.OrderPriority))
            _baggageManager.Set(BaggageManager.Keys.OrderPriority, context.OrderPriority);
            
        if (!string.IsNullOrEmpty(context.Channel))
            _baggageManager.Set(BaggageManager.Keys.Channel, context.Channel);
            
        if (!string.IsNullOrEmpty(context.TransactionId))
            _baggageManager.Set(BaggageManager.Keys.TransactionId, context.TransactionId);
    }
    
    /// <summary>
    /// Updates the average wait time metric for a priority level
    /// </summary>
    private void UpdateAverageWaitTime(int priority, TimeSpan processingTime)
    {
        // Simple moving average calculation
        var currentAverage = _averageWaitTimes[priority];
        var newAverage = TimeSpan.FromTicks((currentAverage.Ticks * 9 + processingTime.Ticks) / 10); // 90% old, 10% new
        _averageWaitTimes[priority] = newAverage;
    }

    public void Dispose()
    {
        _cancellationTokenSource.Cancel();
        try
        {
            _processingTask.Wait(TimeSpan.FromSeconds(5));
        }
        catch
        {
            // Ignore any exceptions during shutdown
        }
        
        _cancellationTokenSource.Dispose();
        _processingSemaphore.Dispose();
    }
}

/// <summary>
/// Represents a queued reservation task with business context
/// </summary>
public class InventoryReservationTask
{
    public InventoryReservationRequest Request { get; set; }
    public int Priority { get; set; }
    public DateTime SubmittedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public BusinessContext BusinessContext { get; set; }
    public string Status { get; set; }
    public string ErrorMessage { get; set; }
    public int? ReservationId { get; set; }
}
