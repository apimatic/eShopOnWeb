using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;


namespace Microsoft.eShopWeb.Infrastructure.Identity;

public class AppIdentityDbContext : IdentityDbContext<ApplicationUser>
{
    public DbSet<MaxioEnrollment> MaxioEnrollments => Set<MaxioEnrollment>();

    public AppIdentityDbContext(DbContextOptions<AppIdentityDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>(user =>
        {
            user.Property(x => x.FirstName).HasMaxLength(100);
            user.Property(x => x.LastName).HasMaxLength(100);
        });

        builder.Entity<MaxioEnrollment>(enrollment =>
        {
            enrollment.ToTable("MaxioEnrollments");
            enrollment.HasKey(x => x.Id);
            enrollment.Property(x => x.ApplicationUserId).HasMaxLength(450).IsRequired();
            enrollment.Property(x => x.ProductHandle).HasMaxLength(255).IsRequired();
            enrollment.Property(x => x.SubscriptionReference).HasMaxLength(80).IsRequired();
            enrollment.Property(x => x.Status).HasMaxLength(32).IsRequired();
            enrollment.Property(x => x.LeaseOwner).HasMaxLength(36);
            enrollment.HasIndex(x => new { x.ApplicationUserId, x.ProductHandle }).IsUnique();
            enrollment.HasIndex(x => x.SubscriptionReference).IsUnique();
            enrollment.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(x => x.ApplicationUserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
