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
            entity.ToTable("SubscriptionEnrollments");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.UserId).HasMaxLength(450).IsRequired();
            entity.Property(x => x.ProductHandle).HasMaxLength(255).IsRequired();
            entity.Property(x => x.CustomerReference).HasMaxLength(80).IsRequired();
            entity.Property(x => x.SubscriptionReference).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(32).IsRequired();
            entity.Property(x => x.LeaseOwner).HasMaxLength(64);
            entity.Property(x => x.LastFailureCode).HasMaxLength(64);
            entity.Property(x => x.Version).IsConcurrencyToken();
            entity.HasIndex(x => new { x.UserId, x.ProductHandle }).IsUnique();
            entity.HasIndex(x => x.SubscriptionReference).IsUnique();
        });
        // Customize the ASP.NET Identity model and override the defaults if needed.
        // For example, you can rename the ASP.NET Identity table names and more.
        // Add your customizations after calling base.OnModelCreating(builder);
    }
}
