using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SubscriptionEnrollmentConfiguration : IEntityTypeConfiguration<SubscriptionEnrollment>
{
    public void Configure(EntityTypeBuilder<SubscriptionEnrollment> builder)
    {
        // The primary key is the duplicate-prevention claim: one enrollment per buyer per plan.
        builder.HasKey(e => new { e.BuyerId, e.PlanHandle });

        builder.Property(e => e.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(e => e.PlanHandle)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(e => e.SubscriptionReference)
            .IsRequired()
            .HasMaxLength(64);

        builder.HasIndex(e => e.SubscriptionReference)
            .IsUnique();

        builder.Property(e => e.Status)
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(e => e.ConcurrencyStamp)
            .IsConcurrencyToken();
    }
}
