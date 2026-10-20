using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class BillingClaimConfiguration : IEntityTypeConfiguration<BillingClaim>
{
    public void Configure(EntityTypeBuilder<BillingClaim> builder)
    {
        // The reference is the primary key on purpose: a key conflict is how a duplicate claim is refused,
        // and every provider (the in-memory one included) enforces primary keys.
        builder.HasKey(c => c.Reference);

        builder.Property(c => c.Reference)
            .HasMaxLength(100);

        builder.Property(c => c.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(c => c.PlanHandle)
            .HasMaxLength(256);

        builder.Property(c => c.ClaimToken)
            .IsConcurrencyToken();

        builder.HasIndex(c => new { c.BuyerId, c.Kind });
    }
}
