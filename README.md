# TenantDbService

A production-quality multi-tenant database microservice for generic SaaS applications, supporting both SQL Server and MongoDB per tenant.

## Architecture Overview

```
┌─────────────────┐    ┌─────────────────┐    ┌─────────────────┐
│   API Gateway   │    │   Load Balancer │    │   Client Apps   │
└─────────────────┘    └─────────────────┘    └─────────────────┘
         │                       │                       │
         └───────────────────────┼───────────────────────┘
                                 │
                    ┌─────────────────┐
                    │ TenantDbService │
                    │   (API Layer)   │
                    └─────────────────┘
                                 │
                    ┌─────────────────┐
                    │   Catalog DB    │
                    │  (SQL Server)   │
                    │  - Tenants      │
                    │  - Connections  │
                    └─────────────────┘
                                 │
         ┌───────────────────────┼───────────────────────┐
         │                       │                       │
┌─────────────────┐    ┌─────────────────┐    ┌─────────────────┐
│ Tenant A SQL    │    │ Tenant B SQL    │    │ Tenant C SQL    │
│ Database        │    │ Database        │    │ Database        │
└─────────────────┘    └─────────────────┘    └─────────────────┘
         │                       │                       │
┌─────────────────┐    ┌─────────────────┐    ┌─────────────────┐
│ Tenant A Mongo  │    │ Tenant B Mongo  │    │ Tenant C Mongo  │
│ Database        │    │ Database        │    │ Database        │
└─────────────────┘    └─────────────────┘    └─────────────────┘
```

### Key Features

- **Database-per-tenant isolation**: Each tenant gets separate SQL Server and MongoDB databases
- **JWT Authentication**: Bearer token authentication with tenant claims
- **Tenant Resolution**: Automatic tenant identification via JWT claims or headers
- **Connection Caching**: In-memory caching for tenant connections (5-minute TTL)
- **Health Monitoring**: Comprehensive health checks for all components
- **Observability**: Built-in metrics, logging, and correlation IDs
- **Load Testing**: k6 scripts for performance validation

## Technology Stack

- **Framework**: .NET 8 Web API (minimal APIs)
- **Databases**: SQL Server 2022, MongoDB 7
- **ORM**: Entity Framework Core (catalog), Dapper (tenant queries)
- **Authentication**: JWT Bearer tokens
- **Containerization**: Docker & Docker Compose
- **Testing**: xUnit, k6 load testing
- **Documentation**: Swagger/OpenAPI

## Quick Start

### Prerequisites

- Docker & Docker Compose
- .NET 8 SDK (for local development)
- Make (optional, for convenience commands)

### 1. Start the Services

```bash
# Clone the repository
git clone <repository-url>
cd TenantDbService

# Start all services
make up
# or
docker-compose -f ops/docker-compose.yml up -d
```

This will start:
- SQL Server 2022 (port 1433)
- MongoDB 7 (port 27017)
- TenantDbService API (port 8080)

### 2. Verify Health

```bash
# Check service health
make health
# or
curl http://localhost:8080/health/live
curl http://localhost:8080/health/ready
```

### 3. Explore the API

Open Swagger UI: http://localhost:8080/swagger

### 4. Create Your First Tenant

```bash
# Create a tenant
curl -X POST http://localhost:8080/tenants \
  -H "Content-Type: application/json" \
  -d '{"name": "My Company"}'

# Response: {"tenantId": "abc123def456"}
```

### 5. Get Authentication Token

```bash
# Get a JWT token for your tenant
curl -X POST http://localhost:8080/auth/dev-token \
  -H "Content-Type: application/json" \
  -d '{"tenantId": "abc123def456"}'

# Response: {"token": "eyJhbGciOiJIUzI1NiIs..."}
```

### 6. Use the API

```bash
# Set your token and tenant ID
export TOKEN="your-jwt-token"
export TENANT_ID="abc123def456"

# Create an order
curl -X POST http://localhost:8080/api/orders \
  -H "Authorization: Bearer $TOKEN" \
  -H "X-Tenant-Id: $TENANT_ID" \
  -H "Content-Type: application/json" \
  -d '{"code": "ORD-001", "amount": 99.99}'

# Get orders
curl -X GET http://localhost:8080/api/orders \
  -H "Authorization: Bearer $TOKEN" \
  -H "X-Tenant-Id: $TENANT_ID"

# Create an event
curl -X POST http://localhost:8080/api/events \
  -H "Authorization: Bearer $TOKEN" \
  -H "X-Tenant-Id: $TENANT_ID" \
  -H "Content-Type: application/json" \
  -d '{"type": "user.login", "payload": {"userId": "123"}}'

# Get events
curl -X GET http://localhost:8080/api/events \
  -H "Authorization: Bearer $TOKEN" \
  -H "X-Tenant-Id: $TENANT_ID"
```

## API Endpoints

### Public Endpoints
- `GET /health/live` - Service liveness check
- `GET /health/ready` - Service readiness check
- `POST /auth/dev-token` - Generate development JWT token
- `POST /tenants` - Create new tenant
- `GET /tenants` - List all tenants

### Protected Endpoints (require JWT + tenant context)
- `GET /api/orders` - List orders for tenant
- `POST /api/orders` - Create new order
- `GET /api/orders/{id}` - Get specific order
- `GET /api/events` - List events for tenant
- `POST /api/events` - Create new event

## Tenant Resolution

The service supports two methods for tenant identification:

1. **JWT Claims**: Include `tenantId` in the JWT token
2. **Header**: Use `X-Tenant-Id` header

```bash
# Method 1: JWT with tenant claim
curl -H "Authorization: Bearer $TOKEN" http://localhost:8080/api/orders

# Method 2: Header (overrides JWT claim)
curl -H "Authorization: Bearer $TOKEN" \
     -H "X-Tenant-Id: $TENANT_ID" \
     http://localhost:8080/api/orders
```

## Data Isolation

Each tenant gets:
- **SQL Server Database**: `tenant_{tenantId}` with Orders table
- **MongoDB Database**: `tenant_{tenantId}` with events collection

Data is completely isolated between tenants. A tenant can only access their own data.

## Development

### Running Tests

```bash
# Run unit tests
make test
# or
dotnet test tests/TenantDbService.Tests/

# Run load tests (requires API to be running)
make load-test
```

### Local Development

```bash
# Build the project
make build

# Run with hot reload
dotnet watch run --project src/TenantDbService.Api/

# Clean build artifacts
make clean
```

### Adding New Features

1. **SQL Server Feature**: Add to `Features/` directory with Dapper repository
2. **MongoDB Feature**: Add to `Features/` directory with MongoDB repository
3. **Update Provisioning**: Modify `ProvisioningService` for new schemas
4. **Add Tests**: Create unit tests in `tests/` directory

## Configuration

### Environment Variables

```json
{
  "ConnectionStrings": {
    "Catalog": "Server=sqlserver,1433;Database=catalog;User Id=sa;Password=P@ssw0rd!;TrustServerCertificate=True;"
  },
  "Jwt": {
    "Issuer": "TenantDbService",
    "Audience": "TenantDbService",
    "Key": "your-super-secret-key-with-at-least-32-characters"
  },
  "SqlServer": {
    "Template": "Server=sqlserver,1433;Database=tenant_{TENANTID};User Id=sa;Password=P@ssw0rd!;TrustServerCertificate=True;"
  },
  "Mongo": {
    "Template": "mongodb://mongodb:27017",
    "DatabaseTemplate": "tenant_{TENANTID}"
  }
}
```

### Production Considerations

- Use strong JWT keys (32+ characters)
- Configure proper connection strings for production databases
- Set up monitoring and alerting
- Use HTTPS in production
- Configure proper logging levels
- Set up database backups

## Load Testing

The project includes k6 load testing scripts to validate performance:

```bash
# Run load tests
make load-test
```

The load test validates:
- Tenant isolation under load
- Performance of tenant B reads while tenant A has heavy writes
- Overall system performance and error rates

## Monitoring & Observability

### Metrics
- Request counters by endpoint and tenant
- Request duration histograms
- Error rates

### Logging
- Structured logging with correlation IDs
- Tenant context in all log entries
- Request/response logging

### Health Checks
- Service liveness (`/health/live`)
- Service readiness (`/health/ready`)
- Database connectivity checks

## Troubleshooting

### Common Issues

1. **Services won't start**: Check Docker is running and ports are available
2. **Database connection errors**: Wait for SQL Server/MongoDB to fully start
3. **JWT validation errors**: Ensure JWT key is properly configured
4. **Tenant not found**: Verify tenant exists and is active

### Logs

```bash
# View all service logs
make logs

# View specific service logs
docker-compose -f ops/docker-compose.yml logs api
docker-compose -f ops/docker-compose.yml logs sqlserver
docker-compose -f ops/docker-compose.yml logs mongodb
```

### Database Access

```bash
# Connect to SQL Server
docker exec -it tenantdbservice-sqlserver /opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P P@ssw0rd!

# Connect to MongoDB
docker exec -it tenantdbservice-mongodb mongosh
```

## Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Add tests for new functionality
5. Run the test suite
6. Submit a pull request

## License

This project is licensed under the MIT License - see the LICENSE file for details.
