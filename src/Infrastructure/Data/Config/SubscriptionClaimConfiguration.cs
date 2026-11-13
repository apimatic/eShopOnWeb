using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SubscriptionClaimConfiguration : IEntityTypeConfiguration<SubscriptionClaim>
{
    public void Configure(EntityTypeBuilder<SubscriptionClaim> builder)
    {
        // Composite primary key (user, plan) is the duplicate guard for subscribe requests.
        builder.HasKey(c => new { c.CustomerReference, c.PlanHandle });

        builder.Property(c => c.CustomerReference)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(c => c.PlanHandle)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(c => c.SubscriptionReference)
            .IsRequired()
            .HasMaxLength(64);

        builder.HasIndex(c => c.SubscriptionReference)
            .IsUnique();

        builder.Property(c => c.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(c => c.ClaimToken)
            .IsRequired()
            .IsConcurrencyToken();
    }
}
