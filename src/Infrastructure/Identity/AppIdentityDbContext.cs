using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;


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
        // Customize the ASP.NET Identity model and override the defaults if needed.
        // For example, you can rename the ASP.NET Identity table names and more.
        // Add your customizations after calling base.OnModelCreating(builder);
        builder.Entity<MaxioSubscriptionMapping>(entity =>
        {
            entity.HasKey(mapping => mapping.Id);
            entity.Property(mapping => mapping.ApplicationUserId).HasMaxLength(450).IsRequired();
            entity.Property(mapping => mapping.SubscriptionReference).HasMaxLength(200).IsRequired();
            entity.Property(mapping => mapping.ProductHandle).HasMaxLength(200).IsRequired();
            entity.HasIndex(mapping => mapping.ApplicationUserId).IsUnique();
            entity.HasIndex(mapping => mapping.SubscriptionReference).IsUnique();
        });
    }
}
