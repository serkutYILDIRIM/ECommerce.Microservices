<#
.SYNOPSIS
    Generates sample traffic to microservices to demonstrate OpenTelemetry instrumentation.

.DESCRIPTION
    This script creates realistic traffic patterns, including:
    - Regular successful requests
    - Errors and failures
    - Slow requests
    - High-volume bursts
    - Requests from different customer tiers

.PARAMETER Duration
    The duration to run the traffic generation in minutes (default: 5)

.PARAMETER BaseUrl
    The base URL for the microservices (default: http://localhost)

.PARAMETER Intensity
    Traffic intensity (Low, Medium, High) (default: Medium)

.EXAMPLE
    .\Generate-SampleTraffic.ps1 -Duration 10 -Intensity High
#>

param (
    [int]$Duration = 5,
    [string]$BaseUrl = "http://localhost",
    [ValidateSet("Low", "Medium", "High")]
    [string]$Intensity = "Medium"
)

# Configure service ports
$productServicePort = 5001
$orderServicePort = 5002
$inventoryServicePort = 5003

# Set request rates based on intensity
$requestRateMultiplier = switch ($Intensity) {
    "Low" { 1 }
    "Medium" { 3 }
    "High" { 10 }
}

# Customer tiers to simulate
$customerTiers = @("Bronze", "Silver", "Gold", "Platinum")

# Product IDs to use in requests
$productIds = @(1..20)

# Initialize counters
$totalRequests = 0
$successRequests = 0
$errorRequests = 0
$slowRequests = 0

# Define endpoints to call
$endpoints = @(
    @{
        "Description" = "Get Products"
        "Method" = "GET"
        "Url" = "$BaseUrl`:$productServicePort/api/products"
        "Weight" = 10
        "ErrorProbability" = 0.01
        "SlowProbability" = 0.05
    },
    @{
        "Description" = "Get Product by ID"
        "Method" = "GET"
        "Url" = "$BaseUrl`:$productServicePort/api/products/{0}"
        "UrlParams" = @($productIds)
        "Weight" = 20
        "ErrorProbability" = 0.02
        "SlowProbability" = 0.1
    },
    @{
        "Description" = "Create Order"
        "Method" = "POST"
        "Url" = "$BaseUrl`:$orderServicePort/api/orders"
        "Body" = @{
            "CustomerId" = 0
            "CustomerTier" = ""
            "Items" = @()
        }
        "Weight" = 5
        "ErrorProbability" = 0.05
        "SlowProbability" = 0.2
    },
    @{
        "Description" = "Get Orders"
        "Method" = "GET"
        "Url" = "$BaseUrl`:$orderServicePort/api/orders"
        "Weight" = 8
        "ErrorProbability" = 0.01
        "SlowProbability" = 0.05
    },
    @{
        "Description" = "Get Inventory"
        "Method" = "GET"
        "Url" = "$BaseUrl`:$inventoryServicePort/api/inventory"
        "Weight" = 15
        "ErrorProbability" = 0.02
        "SlowProbability" = 0.05
    },
    @{
        "Description" = "Get Inventory Item"
        "Method" = "GET"
        "Url" = "$BaseUrl`:$inventoryServicePort/api/inventory/{0}"
        "UrlParams" = @($productIds)
        "Weight" = 12
        "ErrorProbability" = 0.02
        "SlowProbability" = 0.1
    },
    @{
        "Description" = "Reserve Inventory"
        "Method" = "POST"
        "Url" = "$BaseUrl`:$inventoryServicePort/api/inventory/reserve"
        "Body" = @{
            "ProductId" = 0
            "Quantity" = 0
            "OrderId" = ""
        }
        "Weight" = 5
        "ErrorProbability" = 0.1
        "SlowProbability" = 0.15
    },
    @{
        "Description" = "Generate Error"
        "Method" = "GET"
        "Url" = "$BaseUrl`:$productServicePort/api/test/error"
        "Weight" = 1
        "ErrorProbability" = 1.0
        "SlowProbability" = 0.0
    },
    @{
        "Description" = "Generate Slow Request"
        "Method" = "GET"
        "Url" = "$BaseUrl`:$productServicePort/api/test/slow"
        "Weight" = 1
        "ErrorProbability" = 0.0
        "SlowProbability" = 1.0
    }
)

# Calculate total weight for weighted random selection
$totalWeight = ($endpoints | Measure-Object -Property Weight -Sum).Sum

# Create a weighted random selection function
function Get-WeightedRandomEndpoint {
    $randomValue = Get-Random -Minimum 1 -Maximum $totalWeight
    $currentWeight = 0
    
    foreach ($endpoint in $endpoints) {
        $currentWeight += $endpoint.Weight
        if ($randomValue -le $currentWeight) {
            return $endpoint
        }
    }
    
    # Fallback
    return $endpoints[0]
}

# Function to generate a random order
function New-RandomOrder {
    param (
        [string]$CustomerTier
    )
    
    $customerId = Get-Random -Minimum 1000 -Maximum 9999
    $itemCount = Get-Random -Minimum 1 -Maximum 5
    $items = @()
    
    for ($i=0; $i -lt $itemCount; $i++) {
        $productId = $productIds | Get-Random
        $quantity = Get-Random -Minimum 1 -Maximum 10
        $items += @{
            "ProductId" = $productId
            "Quantity" = $quantity
        }
    }
    
    return @{
        "CustomerId" = $customerId
        "CustomerTier" = $CustomerTier
        "Items" = $items
    }
}

# Function to generate a random inventory reservation
function New-RandomReservation {
    $productId = $productIds | Get-Random
    $quantity = Get-Random -Minimum 1 -Maximum 5
    $orderId = [Guid]::NewGuid().ToString()
    
    return @{
        "ProductId" = $productId
        "Quantity" = $quantity
        "OrderId" = $orderId
    }
}

# Function to simulate a request with correlation headers
function Invoke-SimulatedRequest {
    param (
        [hashtable]$Endpoint,
        [string]$CustomerTier = ""
    )
    
    # Generate a unique trace ID
    $traceId = [Guid]::NewGuid().ToString()
    
    # Prepare headers with correlation ID
    $headers = @{
        "x-correlation-id" = $traceId
    }
    
    # Add customer tier header if specified
    if ($CustomerTier -ne "") {
        $headers["x-customer-tier"] = $CustomerTier
    }
    
    # Determine if this will be an error or slow request based on probabilities
    $isError = (Get-Random -Minimum 0.0 -Maximum 1.0) -lt $Endpoint.ErrorProbability
    $isSlow = (Get-Random -Minimum 0.0 -Maximum 1.0) -lt $Endpoint.SlowProbability
    
    # Prepare URL
    $url = $Endpoint.Url
    if ($Endpoint.UrlParams) {
        $paramValue = $Endpoint.UrlParams | Get-Random
        $url = $url -f $paramValue
    }
    
    # Prepare body
    $body = $null
    if ($Endpoint.Body) {
        $body = $Endpoint.Body.Clone()
        
        # If it's an order, generate a random one
        if ($Endpoint.Description -eq "Create Order") {
            $body = New-RandomOrder -CustomerTier $CustomerTier
        }
        
        # If it's a reservation, generate a random one
        if ($Endpoint.Description -eq "Reserve Inventory") {
            $body = New-RandomReservation
        }
        
        $body = $body | ConvertTo-Json
    }
    
    # Add slow query parameter if this should be a slow request
    if ($isSlow) {
        $url = "$url$(if ($url.Contains('?')) { '&' } else { '?' })delay=true"
    }
    
    # Add error query parameter if this should be an error
    if ($isError) {
        $url = "$url$(if ($url.Contains('?')) { '&' } else { '?' })error=true"
    }
    
    $method = $Endpoint.Method
    $description = $Endpoint.Description
    
    try {
        Write-Host "[$method] $description - $url" -ForegroundColor Cyan
        
        $params = @{
            Method = $method
            Uri = $url
            Headers = $headers
            UseBasicParsing = $true
            ErrorAction = 'SilentlyContinue'
        }
        
        if ($body) {
            $params["Body"] = $body
            $params["ContentType"] = "application/json"
        }
        
        $response = Invoke-WebRequest @params
        
        $totalRequests++
        
        if ($isError) {
            $errorRequests++
            Write-Host "  Generated Error (Status: $($response.StatusCode))" -ForegroundColor Red
        }
        elseif ($isSlow) {
            $slowRequests++
            Write-Host "  Slow Request (Status: $($response.StatusCode))" -ForegroundColor Yellow
        }
        else {
            $successRequests++
            Write-Host "  Success (Status: $($response.StatusCode))" -ForegroundColor Green
        }
    }
    catch {
        $totalRequests++
        $errorRequests++
        Write-Host "  Error: $($_.Exception.Message)" -ForegroundColor Red
    }
}

# Function to create traffic patterns that would result in interesting telemetry
function Start-TrafficGeneration {
    $startTime = Get-Date
    $endTime = $startTime.AddMinutes($Duration)
    
    Write-Host "Starting traffic generation at $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -ForegroundColor Green
    Write-Host "Traffic intensity: $Intensity (x$requestRateMultiplier)" -ForegroundColor Green
    Write-Host "Duration: $Duration minutes" -ForegroundColor Green
    Write-Host "Target services:" -ForegroundColor Green
    Write-Host "  - Product Service: $BaseUrl`:$productServicePort" -ForegroundColor Green
    Write-Host "  - Order Service: $BaseUrl`:$orderServicePort" -ForegroundColor Green
    Write-Host "  - Inventory Service: $BaseUrl`:$inventoryServicePort" -ForegroundColor Green
    Write-Host "`n"
    
    while ((Get-Date) -lt $endTime) {
        # Determine number of requests to make in this batch
        $requestCount = Get-Random -Minimum 1 -Maximum ($requestRateMultiplier * 3 + 1)
        
        for ($i = 0; $i -lt $requestCount; $i++) {
            # Select a random customer tier
            $customerTier = $customerTiers | Get-Random
            
            # Select a weighted random endpoint
            $endpoint = Get-WeightedRandomEndpoint
            
            # Make the request
            Invoke-SimulatedRequest -Endpoint $endpoint -CustomerTier $customerTier
            
            # Add a small delay between requests
            Start-Sleep -Milliseconds (Get-Random -Minimum 50 -Maximum 300)
        }
        
        # Simulate traffic bursts occasionally
        if ((Get-Random -Minimum 1 -Maximum 100) -le 10) {
            $burstSize = Get-Random -Minimum 5 -Maximum ($requestRateMultiplier * 5 + 5)
            Write-Host "`nGenerating traffic burst: $burstSize requests" -ForegroundColor Magenta
            
            for ($i = 0; $i -lt $burstSize; $i++) {
                $customerTier = $customerTiers | Get-Random
                $endpoint = Get-WeightedRandomEndpoint
                Invoke-SimulatedRequest -Endpoint $endpoint -CustomerTier $customerTier
                Start-Sleep -Milliseconds (Get-Random -Minimum 10 -Maximum 50)
            }
            
            Write-Host "Burst completed`n" -ForegroundColor Magenta
        }
        
        # Small pause between batches
        Start-Sleep -Milliseconds (Get-Random -Minimum 100 -Maximum 500)
    }
    
    # Print summary
    Write-Host "`nTraffic generation completed at $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -ForegroundColor Green
    Write-Host "Summary:" -ForegroundColor Green
    Write-Host "  Total Requests: $totalRequests" -ForegroundColor Green
    Write-Host "  Successful Requests: $successRequests" -ForegroundColor Green
    Write-Host "  Error Requests: $errorRequests" -ForegroundColor Red
    Write-Host "  Slow Requests: $slowRequests" -ForegroundColor Yellow
}

# Start the traffic generation
Start-TrafficGeneration
