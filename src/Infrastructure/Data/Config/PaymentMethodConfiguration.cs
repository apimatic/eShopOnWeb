using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public sealed class PaymentMethodConfiguration : IEntityTypeConfiguration<PaymentMethod>
{
    public void Configure(EntityTypeBuilder<PaymentMethod> builder)
    {
        builder.Property(method => method.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(method => method.PayPalPaymentTokenId).IsRequired().HasMaxLength(64);
        builder.Property(method => method.PayPalCustomerId).IsRequired().HasMaxLength(64);
        builder.Property(method => method.Brand).IsRequired().HasMaxLength(32);
        builder.Property(method => method.LastDigits).IsRequired().HasMaxLength(4);
        builder.Property(method => method.Expiry).IsRequired().HasMaxLength(7);
        builder.HasIndex(method => method.PayPalPaymentTokenId).IsUnique();
        builder.HasIndex(method => new { method.BuyerId, method.DeletedAt });
    }
}
