using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.Property(p => p.BuyerId).HasMaxLength(256).IsRequired();
        builder.Property(p => p.PayPalPaymentTokenId).HasMaxLength(128).IsRequired();
        builder.Property(p => p.PayPalCustomerId).HasMaxLength(64);
        builder.Property(p => p.LastDigits).HasMaxLength(4).IsRequired();
        builder.Property(p => p.Brand).HasMaxLength(32);
        builder.Property(p => p.Expiry).HasMaxLength(7);
        builder.Property(p => p.CardholderName).HasMaxLength(128);
        builder.HasIndex(p => p.BuyerId);
        builder.HasIndex(p => p.PayPalPaymentTokenId).IsUnique();
    }
}
