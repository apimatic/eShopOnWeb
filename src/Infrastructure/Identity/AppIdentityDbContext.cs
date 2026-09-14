using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;


namespace Microsoft.eShopWeb.Infrastructure.Identity;

public class AppIdentityDbContext : IdentityDbContext<ApplicationUser>
{
    public AppIdentityDbContext(DbContextOptions<AppIdentityDbContext> options)
        : base(options)
    {
    }

    public DbSet<MaxioCustomerLink> MaxioCustomerLinks => Set<MaxioCustomerLink>();
    public DbSet<SubscriptionEnrollment> SubscriptionEnrollments => Set<SubscriptionEnrollment>();

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
            entity.HasKey(link => link.UserId);
            entity.Property(link => link.CustomerReference).HasMaxLength(100).IsRequired();
            entity.Property(link => link.Version).IsConcurrencyToken();
            entity.HasIndex(link => link.CustomerReference).IsUnique();
            entity.HasOne(link => link.User)
                .WithOne()
                .HasForeignKey<MaxioCustomerLink>(link => link.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<SubscriptionEnrollment>(entity =>
        {
            entity.HasKey(enrollment => enrollment.Id);
            entity.Property(enrollment => enrollment.ProductHandle).HasMaxLength(255).IsRequired();
            entity.Property(enrollment => enrollment.SubscriptionReference).HasMaxLength(100).IsRequired();
            entity.Property(enrollment => enrollment.Version).IsConcurrencyToken();
            entity.HasIndex(enrollment => new { enrollment.UserId, enrollment.ProductHandle }).IsUnique();
            entity.HasIndex(enrollment => enrollment.SubscriptionReference).IsUnique();
            entity.HasOne(enrollment => enrollment.User)
                .WithMany()
                .HasForeignKey(enrollment => enrollment.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
