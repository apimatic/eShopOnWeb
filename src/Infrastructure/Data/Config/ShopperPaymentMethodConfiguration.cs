using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class ShopperPaymentMethodConfiguration : IEntityTypeConfiguration<ShopperPaymentMethod>
{
    public void Configure(EntityTypeBuilder<ShopperPaymentMethod> builder)
    {
        builder.ToTable("ShopperPaymentMethods");

        builder.Property(m => m.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(m => m.PayPalCustomerId)
            .IsRequired()
            .HasMaxLength(36);

        builder.Property(m => m.PayPalVaultId)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(m => m.LastDigits)
            .IsRequired()
            .HasMaxLength(4);

        builder.Property(m => m.Brand)
            .HasMaxLength(32);

        builder.Property(m => m.Expiry)
            .HasMaxLength(7);

        builder.Property(m => m.CardholderName)
            .HasMaxLength(300);

        builder.HasIndex(m => m.BuyerId);
        builder.HasIndex(m => m.PayPalVaultId)
            .IsUnique();
    }
}
