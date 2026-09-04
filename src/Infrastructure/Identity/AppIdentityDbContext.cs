using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;


namespace Microsoft.eShopWeb.Infrastructure.Identity;

public class AppIdentityDbContext : IdentityDbContext<ApplicationUser>
{
    public AppIdentityDbContext(DbContextOptions<AppIdentityDbContext> options)
        : base(options)
    {
    }

    public DbSet<SubscriptionMapping> SubscriptionMappings { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        // Customize the ASP.NET Identity model and override the defaults if needed.
        // For example, you can rename the ASP.NET Identity table names and more.
        // Add your customizations after calling base.OnModelCreating(builder);
        builder.Entity<SubscriptionMapping>(entity =>
        {
            entity.HasKey(mapping => mapping.Id);
            entity.Property(mapping => mapping.UserId).HasMaxLength(450).IsRequired();
            entity.Property(mapping => mapping.ProductHandle).HasMaxLength(255).IsRequired();
            entity.Property(mapping => mapping.SubscriptionReference).HasMaxLength(450).IsRequired();
            entity.HasIndex(mapping => new { mapping.UserId, mapping.ProductHandle }).IsUnique();
            entity.HasIndex(mapping => mapping.SubscriptionReference).IsUnique();
        });
    }
}
