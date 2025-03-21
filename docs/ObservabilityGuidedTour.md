# Observability Guided Tour

This document provides a guided tour of the observability features implemented in our microservices architecture using OpenTelemetry. Follow this tour to understand how to use the various observability tools and dashboards to monitor, troubleshoot, and optimize your system.

## Setting Up the Demo

Before starting the tour, set up the demonstration environment:

1. **Start the Infrastructure**:
   ```bash
   docker-compose up -d
   ```

2. **Start the Microservices**:
   ```bash
   cd src/ProductCatalogService
   dotnet run
   
   # In a new terminal
   cd src/OrderProcessingService
   dotnet run
   
   # In a new terminal
   cd src/InventoryManagementService
   dotnet run
   ```

3. **Generate Sample Traffic**:
   ```powershell
   cd scripts
   .\Generate-SampleTraffic.ps1 -Duration 10 -Intensity Medium
   ```

4. **Access Grafana**:
   Open http://localhost:3000 in your browser (default credentials: admin/admin)

## Tour Stops

### Stop 1: Service Health Overview

**Dashboard**: Service Health Dashboard

1. **Open the Dashboard**:
   - Navigate to Dashboards > Service Health Dashboard
   - Set the time range to "Last 15 minutes" (top-right corner)

2. **Explore the Service Health Indicators**:
   - Note the health status of each service
   - Observe the request rates and error rates
   - Check response times by service

3. **Understanding the Dashboard**:
   - Green gauges indicate healthy services
   - Red/yellow indicate issues that need attention
   - The table at the bottom shows detailed performance metrics by service

**What to Look For**:
- Services with higher error rates
- Services with increasing response times
- Correlation between request volume and performance

### Stop 2: Business KPI Analysis

**Dashboard**: Business KPI Dashboard

1. **Open the Dashboard**:
   - Navigate to Dashboards > Business KPI Dashboard
   - Adjust time range if needed

2. **Analyze Business Metrics**:
   - Check order volumes across customer tiers
   - Note inventory utilization rates
   - Examine order completion rates

3. **Filter by Customer Tier**:
   - Use the "Customer Tier" variable at the top
   - Select specific tiers to focus on high-value customers

**What to Look For**:
- Differences in metrics across customer tiers
- Inventory stockout patterns
- Order fulfillment rates dropping during high traffic

### Stop 3: Distributed Tracing Exploration

**Dashboard**: Distributed Tracing

1. **Open the Dashboard**:
   - Navigate to Dashboards > Distributed Tracing

2. **Find Interesting Traces**:
   - Look for traces with high durations
   - Find traces with error status
   - Select a trace to view details

3. **Analyze a Trace**:
   - Expand the trace to see all spans
   - Note the hierarchy of operations
   - Check tags and events on spans
   - Identify slow operations or errors

4. **Service Flow Visualization**:
   - Switch to the Service Graph view
   - See dependencies between services
   - Identify critical paths

**What to Look For**:
- Where time is spent in the transaction
- Error sources in the trace
- Service-to-service communication patterns
- Database operation performance

### Stop 4: Error Analysis

**Dashboard**: Error Tracking

1. **Open the Dashboard**:
   - Navigate to Dashboards > Error Tracking

2. **Review Error Patterns**:
   - Check error rate trends
   - Analyze errors by status code
   - Find the most problematic endpoints

3. **Investigate Error Details**:
   - Click on a high-error time period
   - Drill down to error traces
   - Correlate with log events

**What to Look For**:
- Patterns in error occurrences
- Correlation between error spikes and traffic patterns
- Common root causes for errors

### Stop 5: Performance Analysis

**Dashboard**: Performance Analysis Dashboard

1. **Open the Dashboard**:
   - Navigate to Dashboards > Performance Analysis Dashboard
   - Use the service selector to focus on a specific service

2. **Analyze Service Performance**:
   - Check request rates and response times
   - Review endpoint performance metrics
   - Examine component-level timing

3. **Database and Dependency Analysis**:
   - Check database operation times
   - Review external dependency response times
   - Identify bottlenecks

4. **Resource Utilization**:
   - Check CPU and memory usage
   - Correlate with request patterns

**What to Look For**:
- Slow endpoints and operations
- CPU/memory usage patterns
- External dependencies causing delays
- Database query performance issues

### Stop 6: Composite Metrics Visualization

**Dashboard**: Composite Metrics Dashboard

1. **Open the Dashboard**:
   - Navigate to Dashboards > Composite Metrics Dashboard

2. **Explore Correlated Metrics**:
   - Note how business volume correlates with technical metrics
   - Check health scores against error rates
   - Examine SLA compliance by customer tier

3. **Trace Integration**:
   - Find a trace related to an interesting metric pattern
   - See how it connects metrics, logs, and traces

4. **Log Correlation**:
   - View logs related to specific service issues
   - Note how logs contain trace IDs for correlation

**What to Look For**:
- Relationship between business and technical metrics
- How SLA compliance varies by customer tier
- End-to-end visibility of transactions

### Stop 7: Trace Sampling Analysis

**Dashboard**: Trace Sampling Dashboard

1. **Open the Dashboard**:
   - Navigate to Dashboards > Trace Sampling Dashboard

2. **Understand Sampling Patterns**:
   - Check the sampling rates across services
   - Note differences in sampling for errors vs. normal requests
   - See how high-value operations are sampled more frequently

3. **Evaluate Sampling Effectiveness**:
   - Check rule-based sampling efficiency
   - Review errors by status code distribution

**What to Look For**:
- Effectiveness of sampling strategies
- Coverage of important operations
- Balance between telemetry volume and visibility

## Advanced Analysis Techniques

### Root Cause Analysis Example

1. **Start from a Business Issue**:
   - On the Business KPI Dashboard, identify a drop in order completion rate
   
2. **Correlate with Technical Metrics**:
   - Switch to the Composite Metrics Dashboard
   - Note corresponding increases in error rates or latency
   
3. **Find Problematic Traces**:
   - Go to the Distributed Tracing Dashboard
   - Filter for errors during the affected time period
   
4. **Analyze the Failed Transaction**:
   - Examine the trace to find the failing component
   - Look at span tags and events for error details
   
5. **Check Logs for Context**:
   - Use the trace ID to find related logs
   - Get detailed error messages and stack traces

### Performance Optimization Example

1. **Identify Slow Operations**:
   - Use the Performance Analysis Dashboard
   - Find endpoints with high p95 response times
   
2. **Analyze Component Timing**:
   - Check the "Time Spent by Component" chart
   - Identify the slow component (e.g., database, external service)
   
3. **Find Representative Traces**:
   - Go to Distributed Tracing and find slow traces
   - Analyze where time is being spent
   
4. **Check Resource Utilization**:
   - Correlate with CPU and memory metrics
   - Determine if it's a resource constraint or code issue

## Conclusion

This guided tour demonstrates the power of integrated observability across the three pillars:

1. **Metrics**: For patterns and trends
2. **Traces**: For transaction details and dependencies
3. **Logs**: For contextual information and debugging

By combining these signals and navigating between our custom dashboards, you can:
- Quickly identify issues affecting business outcomes
- Drill down to technical root causes
- Optimize system performance
- Ensure service level objectives are met

The OpenTelemetry implementation in our microservices provides a complete observability solution that helps maintain system health and reliability while supporting business goals.
