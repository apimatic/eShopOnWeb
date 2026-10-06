using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    public void Configure(EntityTypeBuilder<Subscription> builder)
    {
        builder.Property(s => s.UserId)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(s => s.PlanHandle)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(s => s.PlanName)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(s => s.MaxioCustomerReference)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(s => s.MaxioSubscriptionReference)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(s => s.State)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(s => s.Currency)
            .HasMaxLength(8);

        builder.Property(s => s.BillingIntervalUnit)
            .HasMaxLength(16);

        // Idempotency guarantees: a user can hold at most one local record per plan.
        builder.HasIndex(s => new { s.UserId, s.PlanHandle }).IsUnique();
        builder.HasIndex(s => s.MaxioSubscriptionId).IsUnique();
    }
}