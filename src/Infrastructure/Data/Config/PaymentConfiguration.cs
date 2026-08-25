using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.Property(p => p.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(p => p.PayPalOrderId).IsRequired().HasMaxLength(100);
        builder.Property(p => p.AuthorizationId).IsRequired().HasMaxLength(100);
        builder.Property(p => p.AuthorizationStatus).IsRequired().HasMaxLength(50);
        builder.Property(p => p.Currency).IsRequired().HasMaxLength(3);
        builder.Property(p => p.IdempotencyKey).IsRequired().HasMaxLength(100);
        builder.Property(p => p.AuthorizedAmount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.CapturedAmount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.PayPalFee).HasColumnType("decimal(18,2)");
        builder.Property(p => p.NetAmount).HasColumnType("decimal(18,2)");

        var nav = builder.Metadata.FindNavigation(nameof(Payment.Refunds));
        nav?.SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}

public class PaymentRefundConfiguration : IEntityTypeConfiguration<PaymentRefund>
{
    public void Configure(EntityTypeBuilder<PaymentRefund> builder)
    {
        builder.Property(r => r.PayPalRefundId).IsRequired().HasMaxLength(100);
        builder.Property(r => r.IdempotencyKey).IsRequired().HasMaxLength(256);
        builder.Property(r => r.Amount).HasColumnType("decimal(18,2)");
    }
}
