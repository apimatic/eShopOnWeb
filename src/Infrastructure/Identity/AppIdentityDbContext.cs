using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;


namespace Microsoft.eShopWeb.Infrastructure.Identity;

public class AppIdentityDbContext : IdentityDbContext<ApplicationUser>
{
    public AppIdentityDbContext(DbContextOptions<AppIdentityDbContext> options)
        : base(options)
    {
    }

    public DbSet<SubscriptionEnrollment> SubscriptionEnrollments => Set<SubscriptionEnrollment>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<SubscriptionEnrollment>(entity =>
        {
            entity.HasKey(enrollment => enrollment.Id);
            entity.Property(enrollment => enrollment.UserId).HasMaxLength(450).IsRequired();
            entity.Property(enrollment => enrollment.ProductHandle).HasMaxLength(255).IsRequired();
            entity.Property(enrollment => enrollment.MaxioCustomerReference).HasMaxLength(128).IsRequired();
            entity.Property(enrollment => enrollment.MaxioSubscriptionReference).HasMaxLength(128).IsRequired();
            entity.HasIndex(enrollment => new { enrollment.UserId, enrollment.ProductHandle }).IsUnique();
            entity.HasIndex(enrollment => enrollment.MaxioSubscriptionReference).IsUnique();
        });
        // Customize the ASP.NET Identity model and override the defaults if needed.
        // For example, you can rename the ASP.NET Identity table names and more.
        // Add your customizations after calling base.OnModelCreating(builder);
    }
}
