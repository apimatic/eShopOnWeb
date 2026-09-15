using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderPaymentConfiguration : IEntityTypeConfiguration<OrderPayment>
{
    public void Configure(EntityTypeBuilder<OrderPayment> builder)
    {
        builder.HasIndex(x => x.OrderId).IsUnique();
        builder.HasOne<Order>().WithOne().HasForeignKey<OrderPayment>(x => x.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Property(x => x.BuyerId).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Amount).HasColumnType("decimal(18,2)");
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.PayPalOrderId).HasMaxLength(36);
        builder.Property(x => x.PayPalOrderStatus).HasMaxLength(32);
        builder.Property(x => x.AuthorizationId).HasMaxLength(64);
        builder.Property(x => x.AuthorizationStatus).HasMaxLength(32);
        builder.Property(x => x.CaptureId).HasMaxLength(64);
        builder.Property(x => x.CaptureStatus).HasMaxLength(32);
        builder.Property(x => x.CapturedAmount).HasColumnType("decimal(18,2)");
        builder.Property(x => x.PayPalFee).HasColumnType("decimal(18,2)");
        builder.Property(x => x.NetAmount).HasColumnType("decimal(18,2)");
        builder.Property(x => x.CardBrand).HasMaxLength(32);
        builder.Property(x => x.CardLast4).HasMaxLength(4);

        var refunds = builder.Metadata.FindNavigation(nameof(OrderPayment.Refunds));
        refunds?.SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}

public class PaymentRefundConfiguration : IEntityTypeConfiguration<PaymentRefund>
{
    public void Configure(EntityTypeBuilder<PaymentRefund> builder)
    {
        builder.HasIndex(x => new { x.OrderPaymentId, x.IdempotencyKey }).IsUnique();
        builder.Property(x => x.IdempotencyKey).HasMaxLength(108).IsRequired();
        builder.Property(x => x.PayPalRefundId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Amount).HasColumnType("decimal(18,2)");
    }
}
