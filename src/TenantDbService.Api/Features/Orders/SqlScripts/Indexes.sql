-- Indexes for Orders table
-- This script creates performance indexes for the Orders table

-- Index on CreatedAt for sorting by creation date (most common query)
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Orders_CreatedAt')
BEGIN
    CREATE INDEX IX_Orders_CreatedAt ON Orders (CreatedAt DESC)
END

-- Index on Code for filtering by order code
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Orders_Code')
BEGIN
    CREATE INDEX IX_Orders_Code ON Orders (Code)
END

-- Composite index on Code and CreatedAt for filtered sorting
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Orders_Code_CreatedAt')
BEGIN
    CREATE INDEX IX_Orders_Code_CreatedAt ON Orders (Code, CreatedAt DESC)
END

-- Index on Amount for range queries
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Orders_Amount')
BEGIN
    CREATE INDEX IX_Orders_Amount ON Orders (Amount)
END
