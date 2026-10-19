using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SubscriptionEnrollmentConfiguration : IEntityTypeConfiguration<SubscriptionEnrollment>
{
    public void Configure(EntityTypeBuilder<SubscriptionEnrollment> builder)
    {
        // The primary key ("{customer reference}|{plan handle}") is the claim: one per user per plan.
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .IsRequired()
            .HasMaxLength(BillingClaimConstraints.CustomerReferenceMaxLength + BillingClaimConstraints.PlanHandleMaxLength + 1);

        builder.Property(e => e.CustomerReference)
            .IsRequired()
            .HasMaxLength(BillingClaimConstraints.CustomerReferenceMaxLength);

        builder.Property(e => e.PlanHandle)
            .IsRequired()
            .HasMaxLength(BillingClaimConstraints.PlanHandleMaxLength);

        builder.Property(e => e.SubscriptionReference)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(e => e.State)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(e => e.LeaseId)
            .IsConcurrencyToken();

        builder.HasIndex(e => e.SubscriptionReference).IsUnique();
        builder.HasIndex(e => new { e.State, e.UpdatedAt });
    }
}
