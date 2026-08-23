using Microsoft.EntityFrameworkCore;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class SubscriptionBillingDbContext : DbContext
{
    public SubscriptionBillingDbContext(DbContextOptions<SubscriptionBillingDbContext> options)
        : base(options)
    {
    }

    public DbSet<SubscriptionEnrollment> SubscriptionEnrollments => Set<SubscriptionEnrollment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var enrollment = modelBuilder.Entity<SubscriptionEnrollment>();
        enrollment.ToTable("SubscriptionEnrollments");
        enrollment.HasKey(x => x.Id);
        enrollment.Property(x => x.IntegrationScope).HasMaxLength(64).IsRequired();
        enrollment.Property(x => x.UserId).HasMaxLength(450).IsRequired();
        enrollment.Property(x => x.ProductHandle).HasMaxLength(255).IsRequired();
        enrollment.Property(x => x.CustomerReference).HasMaxLength(128).IsRequired();
        enrollment.Property(x => x.SubscriptionReference).HasMaxLength(128).IsRequired();
        enrollment.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        enrollment.Property(x => x.LeaseOwner).HasMaxLength(64);
        enrollment.Property(x => x.ConcurrencyToken).IsConcurrencyToken();
        enrollment.Property(x => x.LastFailureCode).HasMaxLength(64);

        enrollment.HasIndex(x => new { x.IntegrationScope, x.UserId, x.ProductHandle }).IsUnique();
        enrollment.HasIndex(x => new { x.IntegrationScope, x.CustomerReference }).IsUnique();
        enrollment.HasIndex(x => new { x.IntegrationScope, x.SubscriptionReference }).IsUnique();
    }
}
