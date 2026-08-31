using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentMethodConfiguration : IEntityTypeConfiguration<PaymentMethod>
{
    public void Configure(EntityTypeBuilder<PaymentMethod> builder)
    {
        builder.Property(x => x.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(x => x.PayPalPaymentTokenId).IsRequired().HasMaxLength(128);
        builder.Property(x => x.PayPalCustomerId).IsRequired().HasMaxLength(128);
        builder.Property(x => x.Brand).HasMaxLength(32);
        builder.Property(x => x.CardType).HasMaxLength(32);
        builder.Property(x => x.LastDigits).IsRequired().HasMaxLength(4);
        builder.Property(x => x.Expiry).HasMaxLength(7);
        builder.HasIndex(x => x.PayPalPaymentTokenId).IsUnique();
        builder.HasIndex(x => new { x.BuyerId, x.IsActive });
    }
}
