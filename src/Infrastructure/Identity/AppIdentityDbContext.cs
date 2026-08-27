using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;


namespace Microsoft.eShopWeb.Infrastructure.Identity;

public class AppIdentityDbContext : IdentityDbContext<ApplicationUser>
{
    public DbSet<SubscriptionEnrollment> SubscriptionEnrollments => Set<SubscriptionEnrollment>();

    public AppIdentityDbContext(DbContextOptions<AppIdentityDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<SubscriptionEnrollment>(enrollment =>
        {
            enrollment.ToTable("SubscriptionEnrollments");
            enrollment.HasKey(x => x.Id);
            enrollment.Property(x => x.UserId).HasMaxLength(450).IsRequired();
            enrollment.Property(x => x.PlanHandle).HasMaxLength(100).IsRequired();
            enrollment.Property(x => x.SubscriptionReference).HasMaxLength(100).IsRequired();
            enrollment.Property(x => x.Status).HasMaxLength(32).IsRequired();
            enrollment.Property(x => x.LeaseId).HasMaxLength(32);
            enrollment.Property(x => x.ConcurrencyToken).HasMaxLength(32).IsConcurrencyToken().IsRequired();
            enrollment.HasIndex(x => new { x.UserId, x.PlanHandle }).IsUnique();
            enrollment.HasIndex(x => x.SubscriptionReference).IsUnique();
        });
        // Customize the ASP.NET Identity model and override the defaults if needed.
        // For example, you can rename the ASP.NET Identity table names and more.
        // Add your customizations after calling base.OnModelCreating(builder);
    }
}
