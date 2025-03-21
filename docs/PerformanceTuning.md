# Performance Tuning

This document provides guidance on tuning the observability implementation for optimal performance in both development and production environments.

## Observability Performance Impact

Adding observability instrumentation introduces some overhead to your application. The primary factors affecting this overhead are:

1. **Sampling Rate**: Higher sampling rates collect more data but increase overhead
2. **Instrumentation Detail**: Finer-grained instrumentation provides more visibility but adds processing time
3. **Export Frequency**: More frequent exports provide fresher data but consume more resources
4. **Batch Size**: Larger batches are more efficient but may delay data visibility

## Recommended Configurations

### Development Environment

In development, prioritize debugging and visibility over performance:

```json
{
  "OpenTelemetry": {
    "Sampling": {
      "BaseRate": 1.0,
      "ErrorSamplingRate": 1.0,
      "HighValueSamplingRate": 1.0
    },
    "Export": {
      "BatchSize": 512,
      "MaxQueueSize": 2048,
      "ScheduledDelayMilliseconds": 1000
    }
  }
}
```

This configuration:
- Samples all transactions (100% sampling)
- Uses smaller batch sizes for quicker visibility
- Exports frequently for immediate feedback

### Production Environment - Low Volume

For services with lower transaction volumes (<100 req/sec):

```json
{
  "OpenTelemetry": {
    "Sampling": {
      "BaseRate": 0.3,
      "ErrorSamplingRate": 1.0,
      "HighValueSamplingRate": 0.5
    },
    "Export": {
      "BatchSize": 2048,
      "MaxQueueSize": 4096,
      "ScheduledDelayMilliseconds": 5000
    }
  }
}
```

This configuration:
- Samples 30% of normal transactions
- Captures all errors
- Samples 50% of high-value operations
- Uses moderate batch sizes and export frequency

### Production Environment - High Volume

For services with higher transaction volumes (>100 req/sec):

```json
{
  "OpenTelemetry": {
    "Sampling": {
      "BaseRate": 0.05,
      "ErrorSamplingRate": 0.5,
      "HighValueSamplingRate": 0.1
    },
    "Export": {
      "BatchSize": 4096,
      "MaxQueueSize": 8192,
      "ScheduledDelayMilliseconds": 10000
    }
  }
}
```

This configuration:
- Samples only 5% of normal transactions
- Samples 50% of errors
- Samples 10% of high-value operations
- Uses larger batches and less frequent exports

## Benchmarks

Performance impact measurements with different configurations:

| Configuration | CPU Overhead | Memory Overhead | Network Bandwidth |
|---------------|--------------|-----------------|-------------------|
| Development   | 8-10%        | 150-200MB       | 2-5MB/min         |
| Low Volume    | 3-5%         | 80-120MB        | 0.5-2MB/min       |
| High Volume   | 1-2%         | 40-80MB         | 0.2-1MB/min       |

## Optimization Techniques

### 1. Filter Noisy Endpoints

Exclude health checks and metrics endpoints from instrumentation:

```csharp
.AddAspNetCoreInstrumentation(opts =>
{
    opts.Filter = ctx => 
        !ctx.Request.Path.StartsWithSegments("/health") && 
        !ctx.Request.Path.StartsWithSegments("/metrics");
})
```

### 2. Reduce Attribute Cardinality

Limit attribute (tag) cardinality to avoid explosion of time series:

```csharp
// Bad - high cardinality
activity?.SetTag("user.id", userId);

// Better - limit cardinality
activity?.SetTag("user.tier", userTier);
```

### 3. Use Batched Export

Configure exporters to use appropriate batch settings:

```csharp
.AddOtlpExporter(opts =>
{
    opts.BatchExportProcessorOptions = new BatchExportProcessorOptions<Activity>
    {
        MaxQueueSize = 4096,
        MaxExportBatchSize = 2048,
        ScheduledDelayMilliseconds = 5000
    };
})
```

### 4. Implement Periodic Export for High-Volume Metrics

For high-volume metrics, use periodic export rather than continuous:

```csharp
_myCounter.Add(count, 
    new KeyValuePair<string, object?>("dimension1", value1),
    new KeyValuePair<string, object?>("dimension2", value2));
```

### 5. Use Composite Samplers

Use the provided composite sampler to intelligently sample based on value:

```csharp
// Configure in appsettings.json
{
  "OpenTelemetry": {
    "Sampling": {
      "BaseRate": 0.1,
      "ErrorSamplingRate": 1.0,
      "HighValueSamplingRate": 0.5,
      "LatencyThresholdMs": 500
    }
  }
}
```

## Memory Considerations

When tuning for memory usage, consider:

1. **Span Complexity**: Complex spans with many attributes use more memory
2. **Queue Sizes**: Large export queues consume significant memory
3. **Batch Sizes**: Very large batches can cause memory spikes

## CPU Considerations

To optimize CPU usage:

1. **Sampling**: Increase sampling to reduce processing overhead
2. **Attribute Recording**: Minimize attribute computation and recording
3. **Async Processing**: Ensure span processing happens off the critical path

## OpenTelemetry Collector Scaling

For the OpenTelemetry Collector, consider:

1. **Memory Allocation**: Minimum 512MB, recommend 1-2GB for production
2. **CPU Allocation**: At least 1 CPU core, 2-4 for high volume
3. **Disk Space**: Sufficient space for persistent queues if enabled
4. **Network**: Adequate bandwidth for telemetry volume

## Monitoring the Overhead

The OpenTelemetry SDK exposes its own metrics to monitor its performance:

- `otel.exporter.queue`: Export queue metrics
- `otel.exporter.success`: Successful exports
- `otel.exporter.failure`: Failed exports
- `otel.processor.dropped`: Dropped spans due to queue overflow

Monitor these metrics to ensure your telemetry system is running efficiently.
