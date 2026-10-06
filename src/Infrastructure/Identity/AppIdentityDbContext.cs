using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;


namespace Microsoft.eShopWeb.Infrastructure.Identity;

public class AppIdentityDbContext : IdentityDbContext<ApplicationUser>
{
    public AppIdentityDbContext(DbContextOptions<AppIdentityDbContext> options)
        : base(options)
    {
    }

    public DbSet<UserSubscription> UserSubscriptions => Set<UserSubscription>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        // Customize the ASP.NET Identity model and override the defaults if needed.
        // For example, you can rename the ASP.NET Identity table names and more.
        // Add your customizations after calling base.OnModelCreating(builder);

        builder.Entity<UserSubscription>(entity =>
        {
            entity.ToTable("UserSubscriptions");
            entity.Property(u => u.UserId).HasMaxLength(450).IsRequired();
            entity.Property(u => u.ProductHandle).HasMaxLength(100).IsRequired();
            entity.Property(u => u.ProductName).HasMaxLength(200).IsRequired();
            entity.Property(u => u.State).HasMaxLength(50).IsRequired();
            // Enforces idempotent subscribe: one subscription per user per plan.
            entity.HasIndex(u => new { u.UserId, u.ProductHandle }).IsUnique();
            entity.HasIndex(u => u.MaxioSubscriptionId).IsUnique();
        });
    }
}
