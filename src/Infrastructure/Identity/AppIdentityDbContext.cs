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
            entity.HasIndex(x => new { x.UserId, x.PlanHandle }).IsUnique();
            entity.HasIndex(x => x.SubscriptionReference).IsUnique();
            entity.Property(x => x.UserId).HasMaxLength(450).IsRequired();
            entity.Property(x => x.PlanHandle).HasMaxLength(255).IsRequired();
            entity.Property(x => x.SubscriptionReference).HasMaxLength(255).IsRequired();
            entity.Property(x => x.CustomerReference).HasMaxLength(255).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(32).IsRequired();
        });
        // Customize the ASP.NET Identity model and override the defaults if needed.
        // For example, you can rename the ASP.NET Identity table names and more.
        // Add your customizations after calling base.OnModelCreating(builder);
    }
}
