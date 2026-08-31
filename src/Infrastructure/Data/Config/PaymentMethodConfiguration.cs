using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentMethodConfiguration : IEntityTypeConfiguration<PaymentMethod>
{
    public void Configure(EntityTypeBuilder<PaymentMethod> builder)
    {
        builder.Property(p => p.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(p => p.PaypalPaymentTokenId).IsRequired().HasMaxLength(64);
        builder.Property(p => p.Brand).IsRequired().HasMaxLength(32);
        builder.Property(p => p.LastDigits).IsRequired().HasMaxLength(4);
        builder.Property(p => p.Expiry).IsRequired().HasMaxLength(7);
        builder.HasIndex(p => p.PaypalPaymentTokenId).IsUnique();
        builder.HasIndex(p => new { p.BuyerId, p.DeletedAt });
    }
}
