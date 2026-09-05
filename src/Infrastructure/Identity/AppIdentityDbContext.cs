using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;


namespace Microsoft.eShopWeb.Infrastructure.Identity;

public class AppIdentityDbContext : IdentityDbContext<ApplicationUser>
{
    public DbSet<MaxioSubscriptionEnrollment> MaxioSubscriptionEnrollments => Set<MaxioSubscriptionEnrollment>();
    public AppIdentityDbContext(DbContextOptions<AppIdentityDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<MaxioSubscriptionEnrollment>(entity =>
        {
            entity.HasIndex(enrollment => new { enrollment.UserId, enrollment.PlanHandle }).IsUnique();
            entity.HasIndex(enrollment => enrollment.SubscriptionReference).IsUnique();
            entity.Property(enrollment => enrollment.UserId).HasMaxLength(450).IsRequired();
            entity.Property(enrollment => enrollment.PlanHandle).HasMaxLength(255).IsRequired();
            entity.Property(enrollment => enrollment.SubscriptionReference).HasMaxLength(100).IsRequired();
            entity.Property(enrollment => enrollment.UniquenessToken).HasMaxLength(64).IsRequired();
        });
        // Customize the ASP.NET Identity model and override the defaults if needed.
        // For example, you can rename the ASP.NET Identity table names and more.
        // Add your customizations after calling base.OnModelCreating(builder);
    }
}
