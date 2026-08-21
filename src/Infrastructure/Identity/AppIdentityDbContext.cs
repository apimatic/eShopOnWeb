using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;


namespace Microsoft.eShopWeb.Infrastructure.Identity;

public class AppIdentityDbContext : IdentityDbContext<ApplicationUser>
{
    public AppIdentityDbContext(DbContextOptions<AppIdentityDbContext> options)
        : base(options)
    {
    }

    public DbSet<MaxioCustomerLink> MaxioCustomerLinks => Set<MaxioCustomerLink>();
    public DbSet<MaxioSubscriptionLink> MaxioSubscriptionLinks => Set<MaxioSubscriptionLink>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<MaxioCustomerLink>(entity =>
        {
            entity.HasKey(link => link.UserId);
            entity.Property(link => link.UserId).HasMaxLength(450);
            entity.Property(link => link.CustomerReference).HasMaxLength(500);
            entity.HasIndex(link => link.MaxioCustomerId).IsUnique();
            entity.HasIndex(link => link.CustomerReference).IsUnique();
        });

        builder.Entity<MaxioSubscriptionLink>(entity =>
        {
            entity.HasKey(link => new { link.UserId, link.ProductHandle });
            entity.Property(link => link.UserId).HasMaxLength(450);
            entity.Property(link => link.ProductHandle).HasMaxLength(255);
            entity.Property(link => link.SubscriptionReference).HasMaxLength(700);
            entity.HasIndex(link => link.MaxioSubscriptionId).IsUnique();
            entity.HasIndex(link => link.SubscriptionReference).IsUnique();
        });
    }
}
