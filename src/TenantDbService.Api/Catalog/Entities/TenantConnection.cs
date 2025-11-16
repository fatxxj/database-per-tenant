using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TenantDbService.Api.Catalog.Entities;

public class TenantConnections
{
    [Key]
    [StringLength(32)]
    public string TenantId { get; set; } = string.Empty;
    
    [StringLength(500)]
    public string? SqlServerConnectionString { get; set; }
    
    [StringLength(200)]
    public string? MongoDbConnectionString { get; set; }
    
    [StringLength(100)]
    public string? MongoDbDatabaseName { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    [ForeignKey("TenantId")]
    public virtual Tenant? Tenant { get; set; }
}
