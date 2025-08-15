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
    public string Status { get; set; } = "active";
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    // Navigation property
    public virtual TenantConnections? Connections { get; set; }
}
