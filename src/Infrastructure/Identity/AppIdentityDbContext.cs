using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;


namespace Microsoft.eShopWeb.Infrastructure.Identity;

public class AppIdentityDbContext : IdentityDbContext<ApplicationUser>
{
    public DbSet<MaxioCustomerMapping> MaxioCustomerMappings => Set<MaxioCustomerMapping>();
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
        builder.Entity<MaxioCustomerMapping>(entity =>
        {
            entity.HasKey(mapping => mapping.UserId);
            entity.Property(mapping => mapping.UserId).HasMaxLength(450);
            entity.Property(mapping => mapping.CustomerReference).HasMaxLength(100).IsRequired();
        });

        builder.Entity<MaxioSubscriptionMapping>(entity =>
        {
            entity.HasKey(mapping => mapping.Id);
            entity.Property(mapping => mapping.UserId).HasMaxLength(450).IsRequired();
            entity.Property(mapping => mapping.ProductHandle).HasMaxLength(100).IsRequired();
            entity.Property(mapping => mapping.SubscriptionReference).HasMaxLength(100).IsRequired();
            entity.HasIndex(mapping => new { mapping.UserId, mapping.ProductHandle }).IsUnique();
            entity.HasIndex(mapping => mapping.SubscriptionReference).IsUnique();
        });
    }
}
