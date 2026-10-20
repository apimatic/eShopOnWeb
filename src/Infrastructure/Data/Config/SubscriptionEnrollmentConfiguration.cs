using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SubscriptionEnrollmentConfiguration : IEntityTypeConfiguration<SubscriptionEnrollment>
{
    public void Configure(EntityTypeBuilder<SubscriptionEnrollment> builder)
    {
        // The primary key is the duplicate guard: a second enrollment of the same user in the same plan
        // is refused by the store before any request reaches the billing provider.
        builder.HasKey(e => e.SubscriptionReference);

        builder.Property(e => e.SubscriptionReference)
            .HasMaxLength(512);

        builder.Property(e => e.CustomerReference)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(e => e.PlanHandle)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(e => e.Status)
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(e => e.Version)
            .IsConcurrencyToken();

        builder.HasIndex(e => e.CustomerReference);
    }
}
