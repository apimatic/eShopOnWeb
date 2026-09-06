using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    public void Configure(EntityTypeBuilder<Subscription> builder)
    {
        builder.Property(s => s.IdentityId)
            .IsRequired()
            .HasMaxLength(450);

        builder.Property(s => s.PlanHandle)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(s => s.PlanName)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(s => s.Status)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(s => s.PriceInCents)
            .HasPrecision(18, 2);
    }
}
