# TenantDbService PowerShell Build Script
# Cross-platform commands for building, testing, and running the service

param(
    [Parameter(Position=0)]
    [string]$Command = "help"
)

function Show-Help {
    Write-Host "TenantDbService - Multi-tenant Database Service" -ForegroundColor Green
    Write-Host ""
    Write-Host "Available commands:" -ForegroundColor Yellow
    Write-Host "  build     - Build the API project"
    Write-Host "  test      - Run unit tests"
    Write-Host "  up        - Start all services with Docker Compose"
    Write-Host "  down      - Stop all services"
    Write-Host "  logs      - Show logs from all services"
    Write-Host "  restart   - Restart all services"
    Write-Host "  clean     - Clean build artifacts"
    Write-Host "  load-test - Run k6 load tests"
    Write-Host "  swagger   - Open Swagger UI in browser"
    Write-Host "  health    - Check service health"
    Write-Host "  demo      - Full setup and test workflow"
}

function Build-Project {
    Write-Host "Building TenantDbService API..." -ForegroundColor Green
    dotnet build src/TenantDbService.Api/TenantDbService.Api.csproj -c Release
}

function Test-Project {
    Write-Host "Running unit tests..." -ForegroundColor Green
    dotnet test tests/TenantDbService.Tests/TenantDbService.Tests.csproj --verbosity normal
}

function Start-Services {
    Write-Host "Starting TenantDbService with Docker Compose..." -ForegroundColor Green
    docker-compose -f ops/docker-compose.yml up -d
    Write-Host "Services started. API will be available at http://localhost:8080" -ForegroundColor Yellow
    Write-Host "Swagger UI: http://localhost:8080/swagger" -ForegroundColor Yellow
}

function Stop-Services {
    Write-Host "Stopping TenantDbService services..." -ForegroundColor Green
    docker-compose -f ops/docker-compose.yml down
}

function Show-Logs {
    Write-Host "Showing logs from all services..." -ForegroundColor Green
    docker-compose -f ops/docker-compose.yml logs -f
}

function Restart-Services {
    Write-Host "Restarting services..." -ForegroundColor Green
    docker-compose -f ops/docker-compose.yml restart
}

function Clean-Project {
    Write-Host "Cleaning build artifacts..." -ForegroundColor Green
    dotnet clean src/TenantDbService.Api/TenantDbService.Api.csproj
    dotnet clean tests/TenantDbService.Tests/TenantDbService.Tests.csproj
    
    if (Test-Path "src/TenantDbService.Api/bin") {
        Remove-Item -Recurse -Force "src/TenantDbService.Api/bin"
    }
    if (Test-Path "src/TenantDbService.Api/obj") {
        Remove-Item -Recurse -Force "src/TenantDbService.Api/obj"
    }
    if (Test-Path "tests/TenantDbService.Tests/bin") {
        Remove-Item -Recurse -Force "tests/TenantDbService.Tests/bin"
    }
    if (Test-Path "tests/TenantDbService.Tests/obj") {
        Remove-Item -Recurse -Force "tests/TenantDbService.Tests/obj"
    }
}

function Run-LoadTest {
    Write-Host "Running k6 load tests..." -ForegroundColor Green
    Write-Host "Make sure the API is running (./build.ps1 up) before running load tests" -ForegroundColor Yellow
    docker run --rm -i --network host grafana/k6 run - < load/k6/shared_vs_dbpertenant.js
}

function Open-Swagger {
    Write-Host "Opening Swagger UI..." -ForegroundColor Green
    Start-Process "http://localhost:8080/swagger"
}

function Test-Health {
    Write-Host "Checking service health..." -ForegroundColor Green
    try {
        $response = Invoke-WebRequest -Uri "http://localhost:8080/health/live" -UseBasicParsing
        Write-Host "Service is healthy" -ForegroundColor Green
    }
    catch {
        Write-Host "Service not healthy" -ForegroundColor Red
    }
    
    try {
        $response = Invoke-WebRequest -Uri "http://localhost:8080/health/ready" -UseBasicParsing
        Write-Host "Service is ready" -ForegroundColor Green
    }
    catch {
        Write-Host "Service not ready" -ForegroundColor Red
    }
}

function Create-Tenant {
    Write-Host "Creating a new tenant..." -ForegroundColor Green
    $body = @{
        name = "Example Tenant"
    } | ConvertTo-Json
    
    try {
        $response = Invoke-RestMethod -Uri "http://localhost:8080/tenants" -Method POST -Body $body -ContentType "application/json"
        Write-Host "Tenant created: $($response.tenantId)" -ForegroundColor Green
    }
    catch {
        Write-Host "Failed to create tenant: $($_.Exception.Message)" -ForegroundColor Red
    }
}

function Get-Token {
    Write-Host "Getting dev token for demo-tenant..." -ForegroundColor Green
    $body = @{
        tenantId = "demo-tenant"
    } | ConvertTo-Json
    
    try {
        $response = Invoke-RestMethod -Uri "http://localhost:8080/auth/dev-token" -Method POST -Body $body -ContentType "application/json"
        Write-Host "Token generated: $($response.token)" -ForegroundColor Green
    }
    catch {
        Write-Host "Failed to get token: $($_.Exception.Message)" -ForegroundColor Red
    }
}

function Start-Demo {
    Write-Host "Starting demo setup..." -ForegroundColor Green
    Start-Services
    
    Write-Host "Waiting for services to be ready..." -ForegroundColor Yellow
    Start-Sleep -Seconds 30
    
    Write-Host "Creating demo tenant..." -ForegroundColor Green
    $body = @{
        name = "Demo Tenant"
    } | ConvertTo-Json
    
    try {
        $response = Invoke-RestMethod -Uri "http://localhost:8080/tenants" -Method POST -Body $body -ContentType "application/json"
        Write-Host "Demo tenant created: $($response.tenantId)" -ForegroundColor Green
    }
    catch {
        Write-Host "Failed to create demo tenant: $($_.Exception.Message)" -ForegroundColor Red
    }
    
    Write-Host "Demo setup complete!" -ForegroundColor Green
    Write-Host "Visit http://localhost:8080/swagger to explore the API" -ForegroundColor Yellow
}

# Main script logic
switch ($Command.ToLower()) {
    "help" { Show-Help }
    "build" { Build-Project }
    "test" { Test-Project }
    "up" { Start-Services }
    "down" { Stop-Services }
    "logs" { Show-Logs }
    "restart" { Restart-Services }
    "clean" { Clean-Project }
    "load-test" { Run-LoadTest }
    "swagger" { Open-Swagger }
    "health" { Test-Health }
    "create-tenant" { Create-Tenant }
    "get-token" { Get-Token }
    "demo" { Start-Demo }
    default {
        Write-Host "Unknown command: $Command" -ForegroundColor Red
        Show-Help
    }
}
