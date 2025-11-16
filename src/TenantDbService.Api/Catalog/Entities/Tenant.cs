using System.ComponentModel.DataAnnotations;
using TenantDbService.Api.Common;

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
    public string Status { get; set; } = Constants.TenantStatus.Active;
    
    [Required]
    public DatabaseType DatabaseType { get; set; } = DatabaseType.Both;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    [StringLength(50)]
    public string SchemaVersion { get; set; } = Constants.SchemaDefaults.DefaultVersion;
    
    public string? SchemaDefinition { get; set; }
    
    public DateTime? SchemaUpdatedAt { get; set; }
    
    public virtual TenantConnections? Connections { get; set; }
}
