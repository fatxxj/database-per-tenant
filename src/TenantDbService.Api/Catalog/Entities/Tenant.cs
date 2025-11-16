using System.ComponentModel.DataAnnotations;

namespace TenantDbService.Api.Catalog.Entities;

public class Tenant
{
    [Key]
    [StringLength(32)]
    public string Id { get; set; } = string.Empty;
    
    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;
    
    [Required]
    [StringLength(20)]
    public string Status { get; set; } = Common.Constants.TenantStatus.Active;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    [StringLength(50)]
    public string SchemaVersion { get; set; } = Common.Constants.SchemaDefaults.DefaultVersion;
    
    public string? SchemaDefinition { get; set; } // JSON schema definition
    
    public DateTime? SchemaUpdatedAt { get; set; }
    
    // Navigation property
    public virtual TenantConnections? Connections { get; set; }
}
