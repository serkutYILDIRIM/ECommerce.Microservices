# E-Commerce Microservices with OpenTelemetry

This project demonstrates a microservices-based e-commerce application with comprehensive distributed tracing using OpenTelemetry.

## Services

- **Product Catalog Service**: Manages product information
- **Order Processing Service**: Handles order creation and processing
- **Inventory Management Service**: Manages product inventory levels

## Observability Components

- **OpenTelemetry Collector**: Collects and processes telemetry data
- **Jaeger**: Distributed tracing visualization
- **Zipkin**: Alternative distributed tracing visualization
- **Prometheus**: Metrics collection and storage
- **Grafana**: Metrics visualization

## Getting Started

### Prerequisites

- Docker and Docker Compose
- .NET 9 SDK (for local development)

### Running the Application

1. Clone the repository
2. Run the application using Docker Compose:

```bash
docker-compose up -d
```

3. The following services will be available:
   - Product Catalog Service: http://localhost:8001
   - Order Processing Service: http://localhost:8003
   - Inventory Management Service: http://localhost:8005
   - Jaeger UI: http://localhost:16686
   - Zipkin UI: http://localhost:9411
   - Prometheus: http://localhost:9090
   - Grafana: http://localhost:3000

## Using Jaeger for Trace Visualization

Jaeger provides a powerful UI for visualizing and analyzing distributed traces across all services.

### Accessing Jaeger UI

1. Open your browser and navigate to http://localhost:16686
2. The Jaeger UI will load, showing the trace data from all services

### Finding Traces

1. Use the search panel on the left side to filter traces:
   - **Service**: Select a service (e.g., ProductCatalogService)
   - **Operation**: Select a specific operation (e.g., GetAllProducts)
   - **Tags**: Add key-value pairs to filter by specific attributes
   - **Lookback**: Select the time range (e.g., Last 1 hour)
   - **Max Duration** and **Min Duration**: Filter by execution time
   - **Limit Results**: Set the maximum number of traces to return

2. Click "Find Traces" to retrieve matching traces

### Analyzing Traces

1. Click on a trace in the search results to view its details
2. The trace visualization shows:
   - Spans from all services involved in the request
   - Service names and operations
   - Timing information (duration, start time)
   - Parent-child relationships between spans
   - Errors and warnings

3. Click on individual spans to see:
   - Detailed timing information
   - Span attributes (tags)
   - Log events
   - Process information
   - References to other spans

### Tips for Using Jaeger

- Use the minimap at the top to navigate large traces
- Enable the "Service Performance" view to see statistics for service operations
- Use the Compare feature to analyze differences between traces
- Download traces as JSON for offline analysis
- Use the DAG (Directed Acyclic Graph) tab to see service dependencies

## Testing Distributed Tracing

To generate test traces:

1. Create a new order by sending a POST request to the Order Processing Service
2. Check the trace in Jaeger to see how the request flows through all services
3. Use the trace test endpoints for more controlled testing:
   ```
   GET /trace-test                     # View trace info for the current service
   GET /trace-test/chain?targetUrl=... # Test trace propagation between services
   ```

Example commands for testing trace propagation:
```bash
# Test trace from Order Service to Product Catalog Service
curl "http://localhost:8003/trace-test/chain?targetUrl=http://product-catalog-service/trace-test"

# Test trace from Order Service to Inventory Service
curl "http://localhost:8003/trace-test/chain?targetUrl=http://inventory-management-service/trace-test"
```

## Accessing Grafana

Grafana provides a powerful platform for visualizing metrics, logs, and traces.

### Accessing Grafana UI

1. Open your browser and navigate to http://localhost:3000
2. Log in with the default credentials (admin/admin123)

### Grafana Dashboards

The following dashboards are available in Grafana:

1. **Microservices Overview** - General performance metrics
2. **Distributed Tracing** - Trace visualization and analysis
3. **Error Tracking** - Error monitoring and analysis
