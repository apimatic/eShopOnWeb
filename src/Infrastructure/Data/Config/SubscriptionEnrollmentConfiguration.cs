using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SubscriptionEnrollmentConfiguration : IEntityTypeConfiguration<SubscriptionEnrollment>
{
    public void Configure(EntityTypeBuilder<SubscriptionEnrollment> builder)
    {
        // The primary key is the duplicate-subscription guard: one enrollment per user per product family,
        // so a double-submit is refused on insert before the billing system is called.
        builder.HasKey(e => new { e.UserId, e.ProductFamilyHandle });

        builder.Property(e => e.UserId)
            .HasMaxLength(450);

        builder.Property(e => e.ProductFamilyHandle)
            .HasMaxLength(255);

        builder.Property(e => e.PlanHandle)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(e => e.Reference)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(e => e.State)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.HasIndex(e => e.State);
    }
}
