# TenantDbService Makefile
# Cross-platform commands for building, testing, and running the service

.PHONY: help build test clean up down logs restart

# Default target
help:
	@echo "TenantDbService - Multi-tenant Database Service"
	@echo ""
	@echo "Available commands:"
	@echo "  build     - Build the API project"
	@echo "  test      - Run unit tests"
	@echo "  up        - Start all services with Docker Compose"
	@echo "  down      - Stop all services"
	@echo "  logs      - Show logs from all services"
	@echo "  restart   - Restart all services"
	@echo "  clean     - Clean build artifacts"
	@echo "  load-test - Run k6 load tests"
	@echo "  swagger   - Open Swagger UI in browser"

# Build the API project
build:
	@echo "Building TenantDbService API..."
	dotnet build src/TenantDbService.Api/TenantDbService.Api.csproj -c Release

# Run unit tests
test:
	@echo "Running unit tests..."
	dotnet test tests/TenantDbService.Tests/TenantDbService.Tests.csproj --verbosity normal

# Start all services
up:
	@echo "Starting TenantDbService with Docker Compose..."
	docker-compose -f ops/docker-compose.yml up -d
	@echo "Services started. API will be available at http://localhost:8080"
	@echo "Swagger UI: http://localhost:8080/swagger"

# Stop all services
down:
	@echo "Stopping TenantDbService services..."
	docker-compose -f ops/docker-compose.yml down

# Show logs
logs:
	@echo "Showing logs from all services..."
	docker-compose -f ops/docker-compose.yml logs -f

# Restart services
restart:
	@echo "Restarting services..."
	docker-compose -f ops/docker-compose.yml restart

# Clean build artifacts
clean:
	@echo "Cleaning build artifacts..."
	dotnet clean src/TenantDbService.Api/TenantDbService.Api.csproj
	dotnet clean tests/TenantDbService.Tests/TenantDbService.Tests.csproj
	rm -rf src/TenantDbService.Api/bin
	rm -rf src/TenantDbService.Api/obj
	rm -rf tests/TenantDbService.Tests/bin
	rm -rf tests/TenantDbService.Tests/obj

# Run k6 load tests
load-test:
	@echo "Running k6 load tests..."
	@echo "Make sure the API is running (make up) before running load tests"
	docker run --rm -i --network host grafana/k6 run - < load/k6/shared_vs_dbpertenant.js

# Open Swagger UI (cross-platform)
swagger:
	@echo "Opening Swagger UI..."
ifeq ($(OS),Windows_NT)
	start http://localhost:8080/swagger
else
ifeq ($(shell uname),Darwin)
	open http://localhost:8080/swagger
else
	xdg-open http://localhost:8080/swagger
endif
endif

# Health check
health:
	@echo "Checking service health..."
	@curl -f http://localhost:8080/health/live || echo "Service not healthy"
	@curl -f http://localhost:8080/health/ready || echo "Service not ready"

# Create a new tenant (example)
create-tenant:
	@echo "Creating a new tenant..."
	@curl -X POST http://localhost:8080/tenants \
		-H "Content-Type: application/json" \
		-d '{"name": "Example Tenant"}' \
		| jq .

# Get dev token (example)
get-token:
	@echo "Getting dev token for demo-tenant..."
	@curl -X POST http://localhost:8080/auth/dev-token \
		-H "Content-Type: application/json" \
		-d '{"tenantId": "demo-tenant"}' \
		| jq .

# Full setup and test workflow
demo: up
	@echo "Waiting for services to be ready..."
	@sleep 30
	@echo "Creating demo tenant..."
	@curl -X POST http://localhost:8080/tenants \
		-H "Content-Type: application/json" \
		-d '{"name": "Demo Tenant"}' \
		| jq .
	@echo "Demo setup complete!"
	@echo "Visit http://localhost:8080/swagger to explore the API"
