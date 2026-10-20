using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class BillingCustomerClaimConfiguration : IEntityTypeConfiguration<BillingCustomerClaim>
{
    public void Configure(EntityTypeBuilder<BillingCustomerClaim> builder)
    {
        // The reference is the primary key: the store refuses a second claim for the same shopper.
        builder.HasKey(c => c.Reference);

        builder.Property(c => c.Reference)
            .HasMaxLength(100);

        builder.Property(c => c.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.HasIndex(c => c.BuyerId);
    }
}
