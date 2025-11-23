using System.ComponentModel.DataAnnotations;

namespace TenantDbService.Api.Common;

public class SchemaDefinition
{
    [Required]
    public string Version { get; set; } = "1.0";
    
    [Required]
    public string Name { get; set; } = string.Empty;
    
    public string? Description { get; set; }
    
    [Required]
    public List<TableDefinition> Tables { get; set; } = new();
    
    public List<CollectionDefinition> Collections { get; set; } = new();
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class TableDefinition
{
    [Required]
    [StringLength(128)]
    public string Name { get; set; } = string.Empty;
    
    public string? Description { get; set; }
    
    [Required]
    public List<ColumnDefinition> Columns { get; set; } = new();
    
    public List<IndexDefinition> Indexes { get; set; } = new();
    
    public List<ForeignKeyDefinition> ForeignKeys { get; set; } = new();
}

public class ColumnDefinition
{
    [Required]
    [StringLength(128)]
    public string Name { get; set; } = string.Empty;
    
    [Required]
    [StringLength(50)]
    public string DataType { get; set; } = string.Empty;
    
    public int? MaxLength { get; set; }
    
    public int? Precision { get; set; }
    
    public int? Scale { get; set; }
    
    public bool IsPrimaryKey { get; set; }
    
    public bool IsNullable { get; set; } = true;
    
    public bool IsIdentity { get; set; }
    
    public string? DefaultValue { get; set; }
    
    public string? Description { get; set; }
}

public class IndexDefinition
{
    [Required]
    [StringLength(128)]
    public string Name { get; set; } = string.Empty;
    
    [Required]
    public List<string> Columns { get; set; } = new();
    
    public bool IsUnique { get; set; }
    
    public bool IsClustered { get; set; }
    
    public string? Description { get; set; }
}

public class ForeignKeyDefinition
{
    [Required]
    [StringLength(128)]
    public string Name { get; set; } = string.Empty;
    
    [Required]
    public string ReferencedTable { get; set; } = string.Empty;
    
    [Required]
    public List<string> Columns { get; set; } = new();
    
    [Required]
    public List<string> ReferencedColumns { get; set; } = new();
    
    public string? Description { get; set; }
}

public class CollectionDefinition
{
    [Required]
    [StringLength(128)]
    public string Name { get; set; } = string.Empty;
    
    public string? Description { get; set; }
    
    public List<IndexDefinition> Indexes { get; set; } = new();
    
    public string? ValidationSchema { get; set; } // JSON schema for document validation
}

public record CreateTenantRequest(string Name, SchemaDefinition? SchemaDefinition = null);
public record UpdateSchemaRequest(string SchemaDefinition);
public record SchemaValidationRequest(SchemaDefinition SchemaDefinition);
public record SchemaValidationResponse(bool IsValid, List<string> Errors);
