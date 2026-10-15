using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SubscriptionEnrollmentConfiguration : IEntityTypeConfiguration<SubscriptionEnrollment>
{
    public void Configure(EntityTypeBuilder<SubscriptionEnrollment> builder)
    {
        // The composite primary key is the duplicate guard: a second enrollment of the same user in the same plan
        // is refused on insert, before the billing provider is called.
        builder.HasKey(e => new { e.UserName, e.PlanHandle });

        builder.Property(e => e.UserName)
            .HasMaxLength(256);

        builder.Property(e => e.PlanHandle)
            .HasMaxLength(255);

        builder.Property(e => e.SubscriptionReference)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(e => e.Version)
            .IsConcurrencyToken();
    }
}
