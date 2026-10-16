using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SubscriptionEnrollmentConfiguration : IEntityTypeConfiguration<SubscriptionEnrollment>
{
    public void Configure(EntityTypeBuilder<SubscriptionEnrollment> builder)
    {
        // The primary key (buyer | product family | generation) is the claim: a double-submit's second insert is refused.
        builder.HasKey(e => e.ClaimKey);

        builder.Property(e => e.ClaimKey)
            .HasMaxLength(450); // SQL Server index key limit (900 bytes)

        builder.Property(e => e.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(e => e.ProductFamilyHandle)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(e => e.PlanHandle)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(e => e.SubscriptionReference)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(e => e.Status)
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.HasIndex(e => new { e.BuyerId, e.ProductFamilyHandle });
        builder.HasIndex(e => e.Status);
    }
}
