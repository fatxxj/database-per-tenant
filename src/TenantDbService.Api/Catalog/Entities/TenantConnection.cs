using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TenantDbService.Api.Catalog.Entities;

public class TenantConnections
{
    [Key]
    [StringLength(32)]
    public string TenantId { get; set; } = string.Empty;
    
    [Required]
    [StringLength(500)]
    public string SqlServerConnectionString { get; set; } = string.Empty;
    
    [Required]
    [StringLength(200)]
    public string MongoDbConnectionString { get; set; } = string.Empty;
    
    [Required]
    [StringLength(100)]
    public string MongoDbDatabaseName { get; set; } = string.Empty;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    // Navigation property
    [ForeignKey("TenantId")]
    public virtual Tenant? Tenant { get; set; }
}
