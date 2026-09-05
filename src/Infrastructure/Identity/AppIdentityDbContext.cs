using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;


namespace Microsoft.eShopWeb.Infrastructure.Identity;

public class AppIdentityDbContext : IdentityDbContext<ApplicationUser>
{
    public DbSet<MaxioBillingCustomer> MaxioBillingCustomers => Set<MaxioBillingCustomer>();
    public DbSet<MaxioSubscriptionEnrollment> MaxioSubscriptionEnrollments => Set<MaxioSubscriptionEnrollment>();

    public AppIdentityDbContext(DbContextOptions<AppIdentityDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<MaxioBillingCustomer>(entity =>
        {
            entity.HasIndex(customer => customer.UserId).IsUnique();
            entity.HasIndex(customer => customer.Reference).IsUnique();
            entity.Property(customer => customer.UserId).HasMaxLength(450).IsRequired();
            entity.Property(customer => customer.Reference).HasMaxLength(256).IsRequired();
        });

        builder.Entity<MaxioSubscriptionEnrollment>(entity =>
        {
            entity.HasIndex(enrollment => new { enrollment.UserId, enrollment.PlanHandle }).IsUnique();
            entity.HasIndex(enrollment => enrollment.SubscriptionReference).IsUnique();
            entity.Property(enrollment => enrollment.UserId).HasMaxLength(450).IsRequired();
            entity.Property(enrollment => enrollment.PlanHandle).HasMaxLength(256).IsRequired();
            entity.Property(enrollment => enrollment.SubscriptionReference).HasMaxLength(256).IsRequired();
        });
    }
}
