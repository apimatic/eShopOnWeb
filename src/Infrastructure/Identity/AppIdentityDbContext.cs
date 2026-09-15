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
        // Customize the ASP.NET Identity model and override the defaults if needed.
        // For example, you can rename the ASP.NET Identity table names and more.
        builder.Entity<MaxioSubscriptionMapping>(entity =>
        {
            entity.HasKey(mapping => mapping.Id);
            entity.Property(mapping => mapping.UserId).HasMaxLength(450).IsRequired();
            entity.Property(mapping => mapping.PlanHandle).HasMaxLength(255).IsRequired();
            entity.Property(mapping => mapping.CustomerReference).HasMaxLength(255).IsRequired();
            entity.Property(mapping => mapping.SubscriptionReference).HasMaxLength(255).IsRequired();
            entity.HasIndex(mapping => new { mapping.UserId, mapping.PlanHandle }).IsUnique();
            entity.HasIndex(mapping => mapping.SubscriptionReference).IsUnique();
        });
    }
}
