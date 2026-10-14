using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SubscriptionEnrollmentConfiguration : IEntityTypeConfiguration<SubscriptionEnrollment>
{
    public void Configure(EntityTypeBuilder<SubscriptionEnrollment> builder)
    {
        // The primary key is the claim: a second insert for the same buyer + plan is refused by the store.
        builder.HasKey(e => e.Reference);

        builder.Property(e => e.Reference)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(e => e.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(e => e.PlanHandle)
            .IsRequired()
            .HasMaxLength(255);

        builder.HasIndex(e => e.BuyerId);

        builder.Property(e => e.ClaimToken)
            .IsConcurrencyToken();
    }
}
