using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SubscriptionEnrollmentConfiguration : IEntityTypeConfiguration<SubscriptionEnrollment>
{
    public void Configure(EntityTypeBuilder<SubscriptionEnrollment> builder)
    {
        builder.Property(e => e.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(e => e.PlanHandle)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(e => e.CustomerReference)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(e => e.MaxioSubscriptionReference)
            .HasMaxLength(256);

        builder.Property(e => e.State)
            .HasMaxLength(64);

        builder.Property(e => e.Status)
            .IsRequired()
            .HasMaxLength(32);

        // The idempotency backstop: one active enrollment per shopper + plan.
        // A concurrent duplicate subscribe (e.g. a second app instance) cannot insert a second row.
        builder.HasIndex(e => new { e.BuyerId, e.PlanHandle })
            .IsUnique();
    }
}
