using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentMethodConfiguration : IEntityTypeConfiguration<PaymentMethod>
{
    public void Configure(EntityTypeBuilder<PaymentMethod> builder)
    {
        builder.Property(p => p.BuyerId).HasMaxLength(256).IsRequired();
        builder.Property(p => p.PayPalCustomerId).HasMaxLength(64).IsRequired();
        builder.Property(p => p.PayPalTokenId).HasMaxLength(128).IsRequired();
        builder.Property(p => p.CardholderName).HasMaxLength(128);
        builder.Property(p => p.Brand).HasMaxLength(32);
        builder.Property(p => p.LastDigits).HasMaxLength(8);
        builder.Property(p => p.Expiry).HasMaxLength(16);
        builder.HasIndex(p => p.PayPalTokenId).IsUnique();
        builder.HasIndex(p => new { p.BuyerId, p.IsActive });
    }
}
