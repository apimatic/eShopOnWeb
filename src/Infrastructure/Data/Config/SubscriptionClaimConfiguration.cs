using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SubscriptionClaimConfiguration : IEntityTypeConfiguration<SubscriptionClaim>
{
    public void Configure(EntityTypeBuilder<SubscriptionClaim> builder)
    {
        // The reference is the primary key: the store refuses a second claim for the same shopper and plan.
        builder.HasKey(c => c.Reference);

        builder.Property(c => c.Reference)
            .HasMaxLength(100);

        builder.Property(c => c.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(c => c.PlanHandle)
            .IsRequired()
            .HasMaxLength(255);

        builder.HasIndex(c => c.BuyerId);
    }
}
