using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionBillingAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data;

/// <summary>
/// Local persistence for the subscription-billing capability: the buyer→Maxio-customer map and the
/// subscription enrollment records. Kept separate from the Catalog/Identity contexts so the capability is
/// additive. The unique index on <see cref="SubscriptionEnrollment.Reference"/> is the durable duplicate
/// claim that rejects a concurrent double-subscribe (enforced by a relational provider; the in-memory
/// provider used in this dev environment does not enforce indexes).
/// </summary>
public class MaxioBillingContext : DbContext
{
    public MaxioBillingContext(DbContextOptions<MaxioBillingContext> options) : base(options)
    {
    }

    public DbSet<MaxioCustomerLink> CustomerLinks => Set<MaxioCustomerLink>();
    public DbSet<SubscriptionEnrollment> Enrollments => Set<SubscriptionEnrollment>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<MaxioCustomerLink>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.BuyerId).IsRequired().HasMaxLength(256);
            entity.HasIndex(x => x.BuyerId).IsUnique();
        });

        builder.Entity<SubscriptionEnrollment>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.BuyerId).IsRequired().HasMaxLength(256);
            entity.Property(x => x.PlanHandle).IsRequired().HasMaxLength(256);
            entity.Property(x => x.Reference).IsRequired().HasMaxLength(256);
            entity.Property(x => x.State).HasMaxLength(64);
            entity.HasIndex(x => x.Reference).IsUnique();
            entity.HasIndex(x => new { x.BuyerId, x.PlanHandle });
        });
    }
}
