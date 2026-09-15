using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

public class AppIdentityDbContext : IdentityDbContext<ApplicationUser>
{
    public AppIdentityDbContext(DbContextOptions<AppIdentityDbContext> options)
        : base(options)
    {
    }

    public DbSet<MaxioSubscriptionMapping> MaxioSubscriptionMappings => Set<MaxioSubscriptionMapping>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<MaxioSubscriptionMapping>(entity =>
        {
            entity.ToTable("MaxioSubscriptionMappings");
            entity.HasKey(mapping => mapping.Id);
            entity.Property(mapping => mapping.UserId).HasMaxLength(450).IsRequired();
            entity.Property(mapping => mapping.PlanHandle).HasMaxLength(255).IsRequired();
            entity.Property(mapping => mapping.SubscriptionReference).HasMaxLength(450).IsRequired();
            entity.HasIndex(mapping => new { mapping.UserId, mapping.PlanHandle }).IsUnique();
            entity.HasIndex(mapping => mapping.SubscriptionReference).IsUnique();
        });
    }
}
