using Microsoft.EntityFrameworkCore;
using TenantDbService.Api.Catalog.Entities;

namespace TenantDbService.Api.Catalog;

public class CatalogDbContext : DbContext
{
    public CatalogDbContext(DbContextOptions<CatalogDbContext> options) : base(options)
    {
    }

    public DbSet<Tenant> Tenants { get; set; }
    public DbSet<TenantConnections> TenantConnections { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Tenant>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(32);
            entity.Property(e => e.Name).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(20).IsRequired();
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
            
            entity.HasIndex(e => e.Name).IsUnique();
        });

        modelBuilder.Entity<TenantConnections>(entity =>
        {
            entity.HasKey(e => e.TenantId);
            entity.Property(e => e.TenantId).HasMaxLength(32);
            entity.Property(e => e.SqlServerConnectionString).HasMaxLength(500).IsRequired();
            entity.Property(e => e.MongoDbConnectionString).HasMaxLength(200).IsRequired();
            entity.Property(e => e.MongoDbDatabaseName).HasMaxLength(100).IsRequired();
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
            
            entity.HasOne(e => e.Tenant)
                  .WithOne(e => e.Connections)
                  .HasForeignKey<TenantConnections>(e => e.TenantId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
