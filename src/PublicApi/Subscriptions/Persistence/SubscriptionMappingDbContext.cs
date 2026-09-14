using Microsoft.EntityFrameworkCore;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions.Persistence;

/// <summary>
/// Local persistence for the userId → Maxio customer mapping. Uses its own database so it can be
/// created/owned independently of the catalog and identity databases.
/// </summary>
public class SubscriptionMappingDbContext : DbContext
{
    public SubscriptionMappingDbContext(DbContextOptions<SubscriptionMappingDbContext> options)
        : base(options)
    {
    }

    public DbSet<MaxioCustomerLink> CustomerLinks => Set<MaxioCustomerLink>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<MaxioCustomerLink>(entity =>
        {
            entity.ToTable("MaxioCustomerLinks");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.AppUserId).HasMaxLength(450).IsRequired();
            entity.HasIndex(e => e.AppUserId).IsUnique();
        });
    }
}
