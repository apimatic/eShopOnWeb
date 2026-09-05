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
            entity.Property(enrollment => enrollment.UserId).HasMaxLength(450).IsRequired();
            entity.Property(enrollment => enrollment.PlanHandle).HasMaxLength(255).IsRequired();
            entity.Property(enrollment => enrollment.CustomerReference).HasMaxLength(255).IsRequired();
            entity.Property(enrollment => enrollment.SubscriptionReference).HasMaxLength(255).IsRequired();
            entity.Property(enrollment => enrollment.UniquenessToken).HasMaxLength(64).IsRequired();
            entity.Property(enrollment => enrollment.Status).HasMaxLength(32).IsRequired();
            entity.HasIndex(enrollment => new { enrollment.UserId, enrollment.PlanHandle }).IsUnique();
        });
    }
}
