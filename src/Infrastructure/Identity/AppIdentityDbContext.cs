using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;


namespace Microsoft.eShopWeb.Infrastructure.Identity;

public class AppIdentityDbContext : IdentityDbContext<ApplicationUser>
{
    public DbSet<MaxioCustomerLink> MaxioCustomerLinks => Set<MaxioCustomerLink>();
    public DbSet<MaxioSubscriptionEnrollment> MaxioSubscriptionEnrollments => Set<MaxioSubscriptionEnrollment>();

    public AppIdentityDbContext(DbContextOptions<AppIdentityDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(user => user.FirstName).HasMaxLength(100);
            entity.Property(user => user.LastName).HasMaxLength(100);
        });

        builder.Entity<MaxioCustomerLink>(entity =>
        {
            entity.ToTable("MaxioCustomerLinks");
            entity.HasKey(link => link.Id);
            entity.Property(link => link.UserId).HasMaxLength(450).IsRequired();
            entity.Property(link => link.CustomerReference).HasMaxLength(128).IsRequired();
            entity.HasIndex(link => link.UserId).IsUnique();
            entity.HasIndex(link => link.CustomerReference).IsUnique();
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(link => link.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<MaxioSubscriptionEnrollment>(entity =>
        {
            entity.ToTable("MaxioSubscriptionEnrollments");
            entity.HasKey(enrollment => enrollment.Id);
            entity.Property(enrollment => enrollment.UserId).HasMaxLength(450).IsRequired();
            entity.Property(enrollment => enrollment.ProductHandle).HasMaxLength(256).IsRequired();
            entity.Property(enrollment => enrollment.SubscriptionReference).HasMaxLength(128).IsRequired();
            entity.Property(enrollment => enrollment.Status).HasMaxLength(64).IsRequired();
            entity.HasIndex(enrollment => new { enrollment.UserId, enrollment.ProductHandle }).IsUnique();
            entity.HasIndex(enrollment => enrollment.SubscriptionReference).IsUnique();
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(enrollment => enrollment.UserId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
