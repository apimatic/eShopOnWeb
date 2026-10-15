using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SubscriptionEnrollmentConfiguration : IEntityTypeConfiguration<SubscriptionEnrollment>
{
    public void Configure(EntityTypeBuilder<SubscriptionEnrollment> builder)
    {
        // The primary key is the duplicate-subscribe guard: the store refuses a second row for the same user.
        builder.HasKey(e => e.BuyerId);

        builder.Property(e => e.BuyerId)
            .HasMaxLength(450)
            .IsRequired();

        builder.Property(e => e.PlanHandle)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(e => e.SubscriptionReference)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(e => e.Version)
            .IsConcurrencyToken();
    }
}
