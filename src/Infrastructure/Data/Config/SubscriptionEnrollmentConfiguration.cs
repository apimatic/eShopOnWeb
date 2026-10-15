using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SubscriptionEnrollmentConfiguration : IEntityTypeConfiguration<SubscriptionEnrollment>
{
    public void Configure(EntityTypeBuilder<SubscriptionEnrollment> builder)
    {
        // The buyer id is the primary key: the store itself refuses a second enrollment for a buyer.
        builder.HasKey(e => e.BuyerId);

        builder.Property(e => e.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(e => e.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(e => e.PlanHandle)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(e => e.CustomerReference)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(e => e.SubscriptionReference)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(e => e.Version)
            .IsRequired()
            .IsConcurrencyToken();
    }
}
