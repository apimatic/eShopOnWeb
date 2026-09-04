using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;


namespace Microsoft.eShopWeb.Infrastructure.Identity;

public class AppIdentityDbContext : IdentityDbContext<ApplicationUser>
{
    public AppIdentityDbContext(DbContextOptions<AppIdentityDbContext> options)
        : base(options)
    {
    }

    public DbSet<MaxioCustomerLink> MaxioCustomerLinks { get; set; }
    public DbSet<MaxioSubscriptionLink> MaxioSubscriptionLinks { get; set; }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<MaxioCustomerLink>(entity =>
        {
            entity.HasKey(link => link.Id);
            entity.Property(link => link.UserId).IsRequired().HasMaxLength(450);
            entity.Property(link => link.CustomerReference).IsRequired().HasMaxLength(200);
            entity.HasIndex(link => link.UserId).IsUnique();
            entity.HasIndex(link => link.CustomerReference).IsUnique();
        });

        builder.Entity<MaxioSubscriptionLink>(entity =>
        {
            entity.HasKey(link => link.Id);
            entity.Property(link => link.UserId).IsRequired().HasMaxLength(450);
            entity.Property(link => link.ProductHandle).IsRequired().HasMaxLength(200);
            entity.Property(link => link.SubscriptionReference).IsRequired().HasMaxLength(200);
            entity.HasIndex(link => link.MaxioSubscriptionId).IsUnique();
            entity.HasIndex(link => link.SubscriptionReference).IsUnique();
            entity.HasIndex(link => new { link.UserId, link.ProductHandle }).IsUnique();
        });
        // Customize the ASP.NET Identity model and override the defaults if needed.
        // For example, you can rename the ASP.NET Identity table names and more.
        // Add your customizations after calling base.OnModelCreating(builder);
    }
}
