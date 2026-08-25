using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Billing;


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
        builder.Entity<SubscriptionEnrollment>(entity =>
        {
            entity.ToTable("SubscriptionEnrollments");
            entity.HasKey(x => x.Reference);
            entity.Property(x => x.Reference).HasMaxLength(80);
            entity.Property(x => x.UserId).HasMaxLength(450).IsRequired();
            entity.Property(x => x.ProductHandle).HasMaxLength(255).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(32).IsRequired();
            entity.Property(x => x.LeaseId).HasMaxLength(36);
            entity.HasIndex(x => new { x.UserId, x.ProductHandle }).IsUnique();
        });
        // Customize the ASP.NET Identity model and override the defaults if needed.
        // For example, you can rename the ASP.NET Identity table names and more.
        // Add your customizations after calling base.OnModelCreating(builder);
    }
}
