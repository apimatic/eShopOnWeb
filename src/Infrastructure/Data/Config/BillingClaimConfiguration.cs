using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class BillingClaimConfiguration : IEntityTypeConfiguration<BillingClaim>
{
    public void Configure(EntityTypeBuilder<BillingClaim> builder)
    {
        // The claim key is the primary key: a duplicate claim is refused by every provider,
        // including the in-memory one (which does not enforce unique indexes).
        builder.HasKey(c => c.Key);

        builder.Property(c => c.Key)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(c => c.Kind)
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(c => c.Status)
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(c => c.ClaimToken)
            .IsConcurrencyToken();
    }
}
