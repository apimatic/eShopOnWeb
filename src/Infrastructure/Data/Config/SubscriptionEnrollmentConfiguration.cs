using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SubscriptionEnrollmentConfiguration : IEntityTypeConfiguration<SubscriptionEnrollment>
{
    public void Configure(EntityTypeBuilder<SubscriptionEnrollment> builder)
    {
        // The primary key is the duplicate guard: a second claim for the same key is refused by the store.
        builder.HasKey(e => e.EnrollmentKey);

        builder.Property(e => e.EnrollmentKey)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(e => e.CustomerReference)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(e => e.SubscriptionReference)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(e => e.PlanHandle)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(e => e.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(e => e.Version)
            .IsConcurrencyToken();

        builder.Ignore(e => e.IsCompleted);
    }
}
