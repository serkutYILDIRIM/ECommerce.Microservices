<#
.SYNOPSIS
    Runs a complete demonstration scenario for the OpenTelemetry project.

.DESCRIPTION
    This script automates the process of:
    1. Starting the infrastructure (Docker containers)
    2. Starting the microservices
    3. Generating sample traffic
    4. Opening Grafana dashboards

.PARAMETER Duration
    The duration to run the traffic generation in minutes (default: 10)

.PARAMETER Intensity
    Traffic intensity (Low, Medium, High) (default: Medium)

.PARAMETER SkipInfrastructure
    Skip starting the infrastructure containers (default: false)

.PARAMETER SkipServices
    Skip starting the microservices (default: false)

.EXAMPLE
    .\Run-DemoScenario.ps1 -Duration 15 -Intensity High
#>

param (
    [int]$Duration = 10,
    [ValidateSet("Low", "Medium", "High")]
    [string]$Intensity = "Medium",
    [switch]$SkipInfrastructure = $false,
    [switch]$SkipServices = $false
)

# Project root directory
$rootDir = (Get-Item -Path $PSScriptRoot).Parent.FullName

# Function to start infrastructure containers
function Start-Infrastructure {
    Write-Host "Starting infrastructure containers..." -ForegroundColor Cyan
    
    # Navigate to the root directory
    Push-Location $rootDir
    
    try {
        # Run docker-compose
        docker-compose up -d
        
        # Check if containers are running
        $containers = docker-compose ps
        if ($LASTEXITCODE -ne 0 -or -not $containers) {
            Write-Host "Error starting containers. Check docker-compose logs." -ForegroundColor Red
            return $false
        }
        
        Write-Host "Infrastructure containers started successfully." -ForegroundColor Green
        
        # Wait for services to initialize
        Write-Host "Waiting for services to initialize (30 seconds)..." -ForegroundColor Yellow
        Start-Sleep -Seconds 30
        
        return $true
    }
    catch {
        Write-Host "Error starting infrastructure: $_" -ForegroundColor Red
        return $false
    }
    finally {
        # Restore original directory
        Pop-Location
    }
}

# Function to start microservices
function Start-Microservices {
    Write-Host "Starting microservices..." -ForegroundColor Cyan
    
    $services = @(
        @{
            Name = "ProductCatalogService"
            Path = Join-Path $rootDir "src\ProductCatalogService"
            Port = 5001
        },
        @{
            Name = "OrderProcessingService"
            Path = Join-Path $rootDir "src\OrderProcessingService"
            Port = 5002
        },
        @{
            Name = "InventoryManagementService"
            Path = Join-Path $rootDir "src\InventoryManagementService"
            Port = 5003
        }
    )
    
    $processes = @()
    
    foreach ($service in $services) {
        Write-Host "Starting $($service.Name)..." -ForegroundColor Yellow
        
        # Start each service in a new PowerShell window
        $processStartInfo = New-Object System.Diagnostics.ProcessStartInfo
        $processStartInfo.FileName = "powershell.exe"
        $processStartInfo.Arguments = "-Command `"Set-Location '$($service.Path)'; dotnet run`""
        $processStartInfo.WorkingDirectory = $service.Path
        $processStartInfo.UseShellExecute = $true
        
        $process = [System.Diagnostics.Process]::Start($processStartInfo)
        $processes += @{
            Process = $process
            Name = $service.Name
        }
        
        Write-Host "$($service.Name) started with process ID: $($process.Id)" -ForegroundColor Green
    }
    
    # Wait for services to initialize
    Write-Host "Waiting for services to initialize (20 seconds)..." -ForegroundColor Yellow
    Start-Sleep -Seconds 20
    
    # Check if services are responding
    foreach ($service in $services) {
        $url = "http://localhost:$($service.Port)/health"
        try {
            $response = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 5
            if ($response.StatusCode -eq 200) {
                Write-Host "$($service.Name) is healthy and responding at $url" -ForegroundColor Green
            }
            else {
                Write-Host "$($service.Name) returned status code $($response.StatusCode)" -ForegroundColor Yellow
            }
        }
        catch {
            Write-Host "$($service.Name) is not responding at $url" -ForegroundColor Red
        }
    }
    
    return $processes
}

# Function to generate traffic
function Start-TrafficGeneration {
    Write-Host "Starting traffic generation for $Duration minutes with $Intensity intensity..." -ForegroundColor Cyan
    
    # Path to the traffic generation script
    $trafficScriptPath = Join-Path $PSScriptRoot "Generate-SampleTraffic.ps1"
    
    # Run the traffic generation script
    & $trafficScriptPath -Duration $Duration -Intensity $Intensity
}

# Function to open Grafana dashboards
function Open-GrafanaDashboards {
    Write-Host "Opening Grafana in default browser..." -ForegroundColor Cyan
    
    Start-Process "http://localhost:3000"
    
    Write-Host @"
Grafana is now open in your browser. Use these credentials:
Username: admin
Password: admin

Recommended dashboards to explore:
1. Service Health Dashboard
2. Business KPI Dashboard
3. Performance Analysis Dashboard
4. Distributed Tracing
5. Composite Metrics Dashboard

See the Observability Guided Tour document for a step-by-step guide.
"@ -ForegroundColor Green
}

# Main execution flow
$processesToCleanup = @()

try {
    # Start infrastructure if not skipped
    if (-not $SkipInfrastructure) {
        $infrastructureStarted = Start-Infrastructure
        if (-not $infrastructureStarted) {
            throw "Failed to start infrastructure"
        }
    }
    else {
        Write-Host "Skipping infrastructure startup (already running)." -ForegroundColor Yellow
    }
    
    # Start microservices if not skipped
    if (-not $SkipServices) {
        $processesToCleanup = Start-Microservices
    }
    else {
        Write-Host "Skipping microservices startup (already running)." -ForegroundColor Yellow
    }
    
    # Open Grafana dashboards
    Open-GrafanaDashboards
    
    # Generate traffic
    Start-TrafficGeneration
    
    # Provide instructions for exploring the dashboards
    Write-Host @"

Demo scenario completed. The traffic generation has finished.

Next steps:
1. Explore the Grafana dashboards to see the telemetry data
2. Follow the guided tour document: $rootDir\docs\ObservabilityGuidedTour.md
3. Review expected telemetry patterns: $rootDir\docs\ExpectedTelemetryPatterns.md

To clean up resources:
- Close the service terminal windows
- Run: docker-compose down (to stop infrastructure)

"@ -ForegroundColor Cyan
}
catch {
    Write-Host "Error running demo scenario: $_" -ForegroundColor Red
}
finally {
    # Optionally add cleanup code here if needed
    # For this script, we'll leave services running so users can explore dashboards
}
