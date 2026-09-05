using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;


namespace Microsoft.eShopWeb.Infrastructure.Identity;

public class AppIdentityDbContext : IdentityDbContext<ApplicationUser>
{
    public AppIdentityDbContext(DbContextOptions<AppIdentityDbContext> options)
        : base(options)
    {
    }

    /// <summary>
    /// Durable claims for subscription enrollments. The unique user/product key prevents a
    /// second request from creating another Maxio subscription for the same selection.
    /// </summary>
    public DbSet<MaxioSubscriptionEnrollment> MaxioSubscriptionEnrollments => Set<MaxioSubscriptionEnrollment>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<MaxioSubscriptionEnrollment>(entity =>
        {
            entity.HasKey(enrollment => enrollment.Id);
            entity.Property(enrollment => enrollment.UserId).HasMaxLength(450).IsRequired();
            entity.Property(enrollment => enrollment.ProductHandle).HasMaxLength(255).IsRequired();
            entity.Property(enrollment => enrollment.SubscriptionReference).HasMaxLength(512).IsRequired();
            entity.HasIndex(enrollment => new { enrollment.UserId, enrollment.ProductHandle }).IsUnique();
            entity.HasIndex(enrollment => enrollment.SubscriptionReference).IsUnique();
        });
        // Customize the ASP.NET Identity model and override the defaults if needed.
        // For example, you can rename the ASP.NET Identity table names and more.
        // Add your customizations after calling base.OnModelCreating(builder);
    }
}
