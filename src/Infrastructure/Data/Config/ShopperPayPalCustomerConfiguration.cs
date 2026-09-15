using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class ShopperPayPalCustomerConfiguration : IEntityTypeConfiguration<ShopperPayPalCustomer>
{
    public void Configure(EntityTypeBuilder<ShopperPayPalCustomer> builder)
    {
        builder.Property(c => c.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(c => c.PayPalCustomerId)
            .IsRequired()
            .HasMaxLength(128);

        builder.HasIndex(c => c.BuyerId).IsUnique();
    }
}
