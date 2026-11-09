using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.Payments;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class ShopperProfileConfiguration : IEntityTypeConfiguration<ShopperProfile>
{
    public void Configure(EntityTypeBuilder<ShopperProfile> builder)
    {
        builder.Property(p => p.Email).IsRequired().HasMaxLength(256);
        builder.Property(p => p.StaxCustomerId).IsRequired().HasMaxLength(64);
        builder.HasIndex(p => p.Email).IsUnique();
    }
}

public class OrderPaymentConfiguration : IEntityTypeConfiguration<OrderPayment>
{
    public void Configure(EntityTypeBuilder<OrderPayment> builder)
    {
        builder.Property(p => p.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(p => p.PaymentMethodId).IsRequired().HasMaxLength(64);
        builder.Property(p => p.MethodType).IsRequired().HasMaxLength(16);
        builder.Property(p => p.StaxTransactionId).HasMaxLength(64);
        builder.Property(p => p.Status).IsRequired().HasMaxLength(16);
        builder.Property(p => p.RefundStatus).IsRequired().HasMaxLength(16);
        builder.Property(p => p.RefundTransactionId).HasMaxLength(64);
        builder.Property(p => p.LastError).HasMaxLength(1000);
        builder.Property(p => p.RefundLastError).HasMaxLength(1000);
        builder.Property(p => p.Subtotal).HasPrecision(18, 2);
        builder.Property(p => p.Fee).HasPrecision(18, 2);
        builder.Property(p => p.ChargedTotal).HasPrecision(18, 2);
        builder.HasIndex(p => p.OrderId).IsUnique();
    }
}
