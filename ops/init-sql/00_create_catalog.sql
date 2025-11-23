-- Initialize Catalog Database
-- This script creates the catalog database and initial schema

-- Create catalog database if it doesn't exist
IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = 'catalog')
BEGIN
    CREATE DATABASE [catalog]
END
GO

USE [catalog]
GO

-- Create Tenants table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Tenants')
BEGIN
    CREATE TABLE Tenants (
        Id NVARCHAR(32) PRIMARY KEY,
        Name NVARCHAR(100) NOT NULL,
        Status NVARCHAR(20) NOT NULL DEFAULT 'active',
        CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE()
    )
    
    -- Create unique index on Name
    CREATE UNIQUE INDEX IX_Tenants_Name ON Tenants (Name)
END
GO

-- Create TenantConnections table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'TenantConnections')
BEGIN
    CREATE TABLE TenantConnections (
        TenantId NVARCHAR(32) PRIMARY KEY,
        SqlServerConnectionString NVARCHAR(500) NOT NULL,
        MongoDbConnectionString NVARCHAR(200) NOT NULL,
        MongoDbDatabaseName NVARCHAR(100) NOT NULL,
        CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        CONSTRAINT FK_TenantConnections_Tenants FOREIGN KEY (TenantId) REFERENCES Tenants(Id) ON DELETE CASCADE
    )
END
GO

-- Create indexes for better performance
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Tenants_Status')
BEGIN
    CREATE INDEX IX_Tenants_Status ON Tenants (Status)
END
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Tenants_CreatedAt')
BEGIN
    CREATE INDEX IX_Tenants_CreatedAt ON Tenants (CreatedAt)
END
GO

-- Insert a sample tenant for testing (optional)
IF NOT EXISTS (SELECT * FROM Tenants WHERE Name = 'demo-tenant')
BEGIN
    DECLARE @DemoTenantId NVARCHAR(32) = 'demo-tenant';
    
    INSERT INTO Tenants (Id, Name, Status, CreatedAt)
    VALUES (@DemoTenantId, 'demo-tenant', 'active', GETUTCDATE())
    
    INSERT INTO TenantConnections (TenantId, SqlServerConnectionString, MongoDbConnectionString, MongoDbDatabaseName, CreatedAt)
    VALUES (
        @DemoTenantId,
        'Server=sqlserver,1433;Database=tenant_demo-tenant;User Id=sa;Password=P@ssw0rd!;TrustServerCertificate=True;',
        'mongodb://mongodb:27017',
        'tenant_demo-tenant',
        GETUTCDATE()
    )
END
GO

PRINT 'Catalog database initialized successfully'
