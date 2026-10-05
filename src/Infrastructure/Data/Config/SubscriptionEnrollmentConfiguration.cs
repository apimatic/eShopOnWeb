using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SubscriptionEnrollmentConfiguration : IEntityTypeConfiguration<SubscriptionEnrollment>
{
    public void Configure(EntityTypeBuilder<SubscriptionEnrollment> builder)
    {
        // The primary key (user + plan) is the claim: a second row for the same enrollment is refused by the store.
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasMaxLength(450)
            .IsRequired();

        builder.Property(e => e.UserId)
            .HasMaxLength(450)
            .IsRequired();

        builder.Property(e => e.PlanHandle)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(e => e.BillingReference)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(e => e.ClaimToken)
            .IsConcurrencyToken();

        builder.HasIndex(e => e.UserId);
    }
}
