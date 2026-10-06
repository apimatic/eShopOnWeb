using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SubscriptionEnrollmentConfiguration : IEntityTypeConfiguration<SubscriptionEnrollment>
{
    public void Configure(EntityTypeBuilder<SubscriptionEnrollment> builder)
    {
        // The primary key is the duplicate-subscribe guard: a second claim for the same user is refused by the store.
        builder.HasKey(e => e.UserName);

        builder.Property(e => e.UserName)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(e => e.PlanHandle)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(e => e.SubscriptionReference)
            .IsRequired()
            .HasMaxLength(300);

        builder.Property(e => e.Status)
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(e => e.Version)
            .IsConcurrencyToken();
    }
}
