using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SubscriptionEnrollmentConfiguration : IEntityTypeConfiguration<SubscriptionEnrollment>
{
    public void Configure(EntityTypeBuilder<SubscriptionEnrollment> builder)
    {
        // The key is the claim: a second enrollment for the same shopper is refused by the store.
        builder.HasKey(e => e.UserReference);

        builder.Property(e => e.UserReference)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(e => e.UserName)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(e => e.PlanHandle)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(e => e.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(e => e.ClaimToken)
            .IsConcurrencyToken();
    }
}
