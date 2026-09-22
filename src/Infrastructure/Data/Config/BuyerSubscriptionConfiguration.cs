using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class BuyerSubscriptionConfiguration : IEntityTypeConfiguration<BuyerSubscription>
{
    public void Configure(EntityTypeBuilder<BuyerSubscription> builder)
    {
        builder.Property(b => b.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(b => b.PlanHandle)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(b => b.SubscriptionReference)
            .IsRequired()
            .HasMaxLength(512);

        builder.Property(b => b.ProviderState)
            .HasMaxLength(64);

        builder.Property(b => b.Status)
            .HasConversion<int>();

        // Duplicate-claim: exactly one enrollment row per buyer + plan. On SQL Server this UNIQUE index
        // rejects a concurrent second subscribe (the service catches the DbUpdateException and reconciles).
        // The EF InMemory provider mandated on this dev machine does NOT enforce uniqueness — see the plan's
        // §9 caveat — so the dev double-click safety additionally leans on the pre-write existence check and
        // Maxio's own reference reconciliation.
        builder.HasIndex(b => new { b.BuyerId, b.PlanHandle })
            .IsUnique();
    }
}
