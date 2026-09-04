using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;


namespace Microsoft.eShopWeb.Infrastructure.Identity;

public class AppIdentityDbContext : IdentityDbContext<ApplicationUser>
{
    public DbSet<MaxioCustomerLink> MaxioCustomerLinks => Set<MaxioCustomerLink>();
    public DbSet<MaxioSubscriptionLink> MaxioSubscriptionLinks => Set<MaxioSubscriptionLink>();

    public AppIdentityDbContext(DbContextOptions<AppIdentityDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        // Customize the ASP.NET Identity model and override the defaults if needed.
        // For example, you can rename the ASP.NET Identity table names and more.
        // Add your customizations after calling base.OnModelCreating(builder);
        builder.Entity<MaxioCustomerLink>(entity =>
        {
            entity.HasKey(link => link.UserId);
            entity.Property(link => link.UserId).HasMaxLength(450);
            entity.HasIndex(link => link.MaxioCustomerId).IsUnique();
        });

        builder.Entity<MaxioSubscriptionLink>(entity =>
        {
            entity.HasKey(link => link.Id);
            entity.Property(link => link.UserId).HasMaxLength(450).IsRequired();
            entity.Property(link => link.ProductHandle).HasMaxLength(255).IsRequired();
            entity.Property(link => link.Reference).HasMaxLength(450).IsRequired();
            entity.HasIndex(link => link.Reference).IsUnique();
        });
    }
}
