# Expected Telemetry Patterns

This document explains the telemetry patterns you should observe when running the sample traffic generation script. These patterns demonstrate the capabilities of the OpenTelemetry implementation across our microservices.

## Traffic Patterns Generated

The sample traffic script generates the following patterns:

1. **Normal Traffic**: Regular requests with expected latency and success responses
2. **Error Scenarios**: Requests that result in 4xx and 5xx errors
3. **High Latency Operations**: Slow requests that exceed normal response times
4. **Traffic Bursts**: Sudden increases in request volume to test system behavior under load
5. **Customer Tier Distribution**: Requests distributed across different customer tiers
6. **Cross-Service Operations**: Transactions that flow through multiple microservices

## Expected Traces

### End-to-End Transaction Traces

You should observe complete distributed traces for order creation flows:

1. The trace begins at the Order Service (`/api/orders` POST endpoint)
2. Continues to the Product Service to validate products
3. Moves to the Inventory Service to check and reserve inventory
4. Returns to the Order Service to complete the transaction

These traces will have a hierarchy showing:
- Parent spans for the full HTTP request
- Child spans for service-to-service calls
- Database operation spans
- Business logic operation spans

### Error Traces

For error scenarios, look for:
- Traces with `error=true` tag
- Span status set to `Error`
- Exception details recorded in span events
- HTTP status codes in the 4xx or 5xx range
- Special attention to inventory reservation failures due to stock unavailability

### Slow Transaction Traces

For high-latency operations, expect:
- Spans with duration exceeding typical thresholds (e.g., > 500ms)
- Higher sampling rates for these spans (due to the latency-based sampler)
- Root cause visible in the span hierarchy (e.g., slow database queries, external service calls)

## Expected Metrics

### Request Rate Metrics

Observe these request rate patterns:
- Base traffic following a consistent pattern with some natural variation
- Periodic traffic bursts showing as spikes in request rate
- Different request rates across endpoints based on weighting in the script

Metric to watch: `http_server_duration_count` rate

### Latency Metrics

The latency distribution should show:
- Most requests within normal parameters
- A distinct group of slow requests (those with the `delay=true` parameter)
- Variation in p50 vs p95 vs p99 percentiles
- Different latency profiles for different endpoints

Metrics to watch: `http_server_duration` histogram quantiles

### Error Rate Metrics

Error patterns should include:
- Higher error rates for inventory reservation than other operations
- Spikes in error rates during burst periods
- Correlation between error rates and latency increases

Metric to watch: `http_server_duration_count{status_code=~"5.."}` / total requests

### Business Metrics

Business metrics should display:
- Order creation rates with distribution across customer tiers
- Inventory levels decreasing as orders are placed
- Inventory reservations increasing as order volume grows

Metrics to watch: `order_created_total`, `inventory_items_available`, `inventory_items_reserved`

## Expected Logs

### Context-Enriched Logs

All logs should contain:
- TraceID and SpanID for correlation with traces
- Service name and instance information
- Structured format with consistent fields

### Error Logs

Error logs should include:
- Exception stack traces
- Detailed error messages
- Context about the operation that failed
- Customer tier information (from baggage)

### Operation Logs

Operation logs should show:
- Logs from different services for the same transaction (linked by TraceID)
- Business context such as order IDs, product IDs, and customer information
- Progression of operations in chronological order

## Cross-Signal Correlation Patterns

One of the most powerful aspects of our OpenTelemetry implementation is the ability to correlate across signals:

1. **Trace-to-Metrics Correlation**: 
   - Spikes in latency metrics should correspond to traces with longer durations
   - Error rate increases should match traces with error statuses

2. **Trace-to-Logs Correlation**: 
   - Each trace should have corresponding logs with matching TraceID
   - Error traces should have detailed error logs

3. **Metrics-to-Logs Correlation**:
   - Metric anomalies should have corresponding log entries explaining the behavior
   - Resource utilization metrics should correlate with performance-related logs

## Dashboard Visualizations

The provided Grafana dashboards should visualize these patterns:

1. **Business KPI Dashboard**: 
   - Shows order rates by customer tier
   - Displays inventory levels and reservation rates
   - Indicates order fulfillment rates

2. **Performance Analysis Dashboard**:
   - Reveals endpoint performance issues
   - Shows service dependencies and bottlenecks
   - Highlights database and external call performance

3. **Service Health Dashboard**:
   - Displays overall health scores that degrade during error scenarios
   - Shows resource utilization during traffic bursts
   - Provides quick identification of problematic services

4. **Composite Metrics Dashboard**:
   - Correlates business and technical metrics
   - Shows impact of performance issues on business KPIs
   - Provides context-aware views filtered by customer tier

## Using Telemetry for Problem Identification

When analyzing the telemetry data, you should be able to:

1. Identify which operations are consistently slow (look for database operations)
2. Determine which services experience errors during traffic bursts (likely the inventory service)
3. See how customer tier affects performance (platinum tier should have priority)
4. Understand the resource utilization impact of increased traffic
5. Correlate business metrics degradation with technical issues

By examining the patterns described above, you'll gain a comprehensive understanding of how observability works in our microservices architecture.
