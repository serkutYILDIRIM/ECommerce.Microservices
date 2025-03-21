# OpenTelemetry Concepts

This document explains the key OpenTelemetry concepts implemented in this project and how they're applied to the microservices architecture.

## What is OpenTelemetry?

OpenTelemetry is an observability framework and toolkit designed to create and manage telemetry data such as traces, metrics, and logs. It provides vendor-neutral APIs, SDKs, and tools to instrument, generate, collect, and export telemetry data for analysis.

The project follows the [OpenTelemetry specification](https://github.com/open-telemetry/opentelemetry-specification) to ensure standardized, portable telemetry data.

## Key Components in Our Implementation

### 1. Distributed Tracing

Distributed tracing tracks the progression of a request across service boundaries, providing visibility into the entire transaction lifecycle.

#### Implementation Details

- **Activity API**: We use .NET's `System.Diagnostics.Activity` as the implementation of OpenTelemetry's `Span` concept
- **Context Propagation**: W3C TraceContext and Baggage headers propagate context between services
- **Manual Instrumentation**: Custom spans for business operations via `ActivitySource`
- **Automatic Instrumentation**: ASP.NET, HttpClient, and Entity Framework instrumentation

#### Code Example

```csharp
// Creating a span
using var activity = source.StartActivity("ProcessOrder");
activity?.SetTag("order.id", orderId);
activity?.SetTag("customer.tier", customerTier);

// Adding events
activity?.AddEvent(new ActivityEvent("OrderValidated"));

// Recording errors
activity?.RecordException(exception);
```

### 2. Metrics

Metrics provide quantitative data about service performance and business operations.

#### Types of Metrics Implemented

- **Counter**: Cumulative metrics that only increase (e.g., request count)
- **Gauge**: Metrics that can increase and decrease (e.g., active requests)
- **Histogram**: Distribution of values (e.g., request duration)

#### Metric Categories

1. **RED Metrics**: Rate, Error, Duration for all services
2. **USE Metrics**: Utilization, Saturation, Errors for resources
3. **Business Metrics**: Order counts, inventory levels, user actions

#### Code Example

```csharp
// Counter example
_orderCounter.Add(1, new KeyValuePair<string, object?>("status", status),
                     new KeyValuePair<string, object?>("customer_tier", customerTier));

// Histogram example
_orderProcessingTime.Record(processingTime.TotalMilliseconds,
                           new KeyValuePair<string, object?>("customer_tier", customerTier));
```

### 3. Logging

Our logging implementation integrates with OpenTelemetry to provide context-enriched logs.

#### Features

- **Structured Logging**: JSON-formatted logs with consistent properties
- **Trace Context Enrichment**: Logs contain trace and span IDs
- **Baggage Propagation**: Business context automatically added to logs
- **Log Sampling**: Log verbosity increases for sampled traces

#### Code Example

```csharp
// Context-aware logging
logger.LogInformation("Processing order {OrderId} for customer tier {CustomerTier}. Trace ID: {TraceId}",
    orderId, customerTier, Activity.Current?.TraceId);
```

### 4. Semantic Conventions

We follow [OpenTelemetry Semantic Conventions](https://github.com/open-telemetry/opentelemetry-specification/blob/main/specification/trace/semantic_conventions/README.md) for consistent naming:

- **Span Names**: `<operation_name>` format (e.g., "ProcessOrder")
- **Attribute Names**: Dot notation namespaces (e.g., "http.method", "db.system")
- **Resource Attributes**: Service identification (e.g., "service.name", "service.version")

### 5. Sampling Strategies

We implement several sampling strategies to balance telemetry volume with observability needs:

#### Implemented Strategies

1. **Parent-Based Sampling**: Maintains trace cohesion by following parent sampling decisions
2. **Probability Sampling**: Base rate for all transactions (default: 10%)
3. **Rule-Based Sampling**: Higher rates for specific endpoints or operations
4. **Adaptive Sampling**: Dynamically adjusts based on system load

#### Custom Samplers

- **Error Sampler**: Always samples error transactions
- **Latency Sampler**: Higher rates for slow transactions
- **Value Sampler**: Higher rates for high-business-value transactions

### 6. OpenTelemetry Collector

The OpenTelemetry Collector acts as a centralized telemetry processing agent:

#### Configured Pipelines

1. **Traces Pipeline**:
   - Receives spans via OTLP
   - Processes with batch processor
   - Exports to Tempo
   
2. **Metrics Pipeline**:
   - Receives metrics via OTLP
   - Aggregates and processes
   - Exports to Prometheus

3. **Logs Pipeline**:
   - Receives logs via OTLP
   - Formats and enhances
   - Exports to Loki

### 7. Backend Storage and Visualization

Our telemetry backend consists of:

- **Prometheus**: Time-series metrics database
- **Tempo**: Distributed tracing backend
- **Loki**: Log aggregation system
- **Grafana**: Unified visualization platform

## OpenTelemetry Best Practices Used

1. **Consistent Naming**: Following semantic conventions for spans, metrics, and attributes
2. **Appropriate Granularity**: Spans at the right level (not too coarse or fine-grained)
3. **Context Propagation**: Ensuring trace context flows through all components
4. **Error Handling**: Properly recording exceptions and error states
5. **Sampling Strategy**: Intelligent sampling to reduce volume while maintaining visibility
6. **Resource Attribution**: Clear identification of services and versions
7. **Correlation**: Connecting traces, metrics, and logs with common identifiers

## Extended Concepts

### Baggage

Baggage is a mechanism to propagate key-value pairs alongside the trace context. We use it to:

1. Carry customer tier information across service boundaries
2. Propagate tenant/user IDs for request attribution
3. Track business context like order types or campaign IDs

### Resource Detection

We implement automatic resource detection to capture:

- Host information (hostname, IP)
- Container metadata (when running in containers)
- Cloud provider details (when running in cloud environments)

### Manual vs. Auto-Instrumentation

Our approach combines:

1. **Auto-instrumentation** for standard components:
   - HTTP servers and clients
   - Database access
   - Dependency calls

2. **Manual instrumentation** for:
   - Business operations
   - Background processing
   - Queue operations
   - Custom components

## Implementation Examples

See the following files for implementation details:

- `Shared.Library/Telemetry/TelemetryExtensions.cs`: Core setup
- `Shared.Library/Telemetry/Processors/CustomSpanProcessor.cs`: Custom processing
- `Shared.Library/Metrics/MetricsExtensions.cs`: Metrics configuration
- `Shared.Library/Logging/LoggingExtensions.cs`: Logging integration
- `Shared.Library/Telemetry/Sampling/CustomSamplers.cs`: Sampling strategies
