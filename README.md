# TenantDbService

A production-quality multi-tenant database microservice for generic SaaS applications. Provides database-per-tenant isolation with flexible database type selection: SQL Server only, MongoDB only, or both.

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────┐
│                         Client Apps                              │
│              (SQL-only, MongoDB-only, or Hybrid)                 │
└─────────────────────────────────────────────────────────────────┘
                                 │
                    ┌─────────────────┐
                    │ TenantDbService │
                    │   (API Layer)   │
                    │  Multi-Tenancy  │
                    │   Microservice  │
                    └─────────────────┘
                                 │
                    ┌─────────────────┐
                    │   Catalog DB    │
                    │  (SQL Server)   │
                    │  - Tenants      │
                    │  - Connections  │
                    │  - DB Types     │
                    └─────────────────┘
                                 │
         ┌───────────────────────┼───────────────────────┐
         │                       │                       │
┌─────────────────┐    ┌─────────────────┐    ┌─────────────────┐
│ Tenant A        │    │ Tenant B        │    │ Tenant C        │
│ (SQL Only)      │    │ (MongoDB Only)  │    │ (Both)          │
│                 │    │                 │    │                 │
│ ┌─────────────┐ │    │ ┌─────────────┐ │    │ ┌─────────────┐ │
│ │ SQL Server  │ │    │ │  MongoDB    │ │    │ │ SQL Server  │ │
│ │  Database   │ │    │ │  Database   │ │    │ │  Database   │ │
│ └─────────────┘ │    │ └─────────────┘ │    │ └─────────────┘ │
│                 │    │                 │    │ ┌─────────────┐ │
│                 │    │                 │    │ │  MongoDB    │ │
│                 │    │                 │    │ │  Database   │ │
│                 │    │                 │    │ └─────────────┘ │
└─────────────────┘    └─────────────────┘    └─────────────────┘
```

### Key Features

- **Flexible Database Selection**: Choose SQL Server, MongoDB, or both per tenant
- **Database-per-tenant isolation**: Complete data separation with dedicated databases
- **Multi-SaaS Support**: Designed to serve multiple SaaS applications with different database needs
- **JWT Authentication**: Bearer token authentication with tenant claims
- **Tenant Resolution**: Automatic tenant identification via JWT claims or headers
- **Connection Caching**: In-memory caching for tenant connections (5-minute TTL)
- **Dynamic Schema Management**: Create tables and collections on-demand
- **Health Monitoring**: Comprehensive health checks for all components
- **Observability**: Built-in metrics, logging, and correlation IDs
- **Production-Ready**: Security best practices, error handling, and rollback mechanisms

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

You can create tenants with different database types:

```bash
# Create a tenant with SQL Server only
curl -X POST http://localhost:8080/tenants \
  -H "Content-Type: application/json" \
  -d '{"name": "SQL Company", "databaseType": 1}'

# Create a tenant with MongoDB only
curl -X POST http://localhost:8080/tenants \
  -H "Content-Type: application/json" \
  -d '{"name": "Mongo Company", "databaseType": 2}'

# Create a tenant with both databases (default)
curl -X POST http://localhost:8080/tenants \
  -H "Content-Type: application/json" \
  -d '{"name": "Hybrid Company", "databaseType": 3}'

# Response: {"tenantId": "abc123def456", "databaseType": "SqlServer"}
```

**Database Type Values:**
- `1` = SQL Server only
- `2` = MongoDB only  
- `3` = Both (default if not specified)

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

Each tenant gets dedicated databases based on their selected database type:

**SQL Server Only:**
- SQL Server Database: `tenant_{tenantId}`
- Tables: Orders (default), plus any custom tables from schema definition

**MongoDB Only:**
- MongoDB Database: `tenant_{tenantId}`
- Collections: events, plus any custom collections from schema definition

**Both Databases:**
- SQL Server Database: `tenant_{tenantId}` for relational data
- MongoDB Database: `tenant_{tenantId}` for document data

Data is completely isolated between tenants. A tenant can only access their own data through their provisioned database type(s).

## Use Cases

### SQL Server Only
Perfect for SaaS applications that:
- Require ACID transactions and relational integrity
- Use structured data with complex relationships
- Need advanced query capabilities (joins, aggregations)
- Examples: ERP systems, CRM platforms, financial applications

### MongoDB Only
Ideal for SaaS applications that:
- Work with flexible, document-based data
- Need high-write throughput for events/logs
- Require schema flexibility and rapid iteration
- Examples: Analytics platforms, IoT applications, content management systems

### Both Databases
Best for SaaS applications that:
- Have mixed data requirements (structured + flexible)
- Store transactional data in SQL + events/logs in MongoDB
- Need different databases for different features
- Examples: E-commerce platforms, healthcare systems, complex enterprise applications

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
- Choose appropriate database types for each tenant based on their needs
- Only provision required database types to optimize resource usage
- Set up monitoring and alerting for both SQL Server and MongoDB
- Use HTTPS in production
- Configure proper logging levels
- Set up database backups for all provisioned databases
- Monitor database costs - tenants only pay for what they use

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
