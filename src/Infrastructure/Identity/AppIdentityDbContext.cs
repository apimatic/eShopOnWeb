using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;


namespace Microsoft.eShopWeb.Infrastructure.Identity;

public class AppIdentityDbContext : IdentityDbContext<ApplicationUser>
{
    public DbSet<MaxioSubscriptionMapping> MaxioSubscriptionMappings => Set<MaxioSubscriptionMapping>();

    public AppIdentityDbContext(DbContextOptions<AppIdentityDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<MaxioSubscriptionMapping>(entity =>
        {
            entity.HasKey(mapping => mapping.Id);
            entity.Property(mapping => mapping.UserId).HasMaxLength(450).IsRequired();
            entity.Property(mapping => mapping.CustomerReference).HasMaxLength(450).IsRequired();
            entity.Property(mapping => mapping.SubscriptionReference).HasMaxLength(450).IsRequired();
            entity.Property(mapping => mapping.ProductHandle).HasMaxLength(255).IsRequired();
            entity.HasIndex(mapping => new { mapping.UserId, mapping.ProductHandle }).IsUnique();
            entity.HasIndex(mapping => mapping.SubscriptionReference).IsUnique();
            entity.HasIndex(mapping => mapping.UserId);
        });
    }
}
