using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class BillingCustomerConfiguration : IEntityTypeConfiguration<BillingCustomer>
{
    public void Configure(EntityTypeBuilder<BillingCustomer> builder)
    {
        // The primary key is the duplicate-prevention claim: one billing customer per buyer.
        builder.HasKey(c => c.BuyerId);

        builder.Property(c => c.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(c => c.CustomerReference)
            .IsRequired()
            .HasMaxLength(64);

        builder.HasIndex(c => c.CustomerReference)
            .IsUnique();

        builder.Ignore(c => c.IsLinked);
    }
}
