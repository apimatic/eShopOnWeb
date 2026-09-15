using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderPaymentConfiguration : IEntityTypeConfiguration<OrderPayment>
{
    public void Configure(EntityTypeBuilder<OrderPayment> builder)
    {
        builder.Property(x => x.Currency).IsRequired().HasMaxLength(3);
        builder.Property(x => x.Amount).HasColumnType("decimal(18,2)");
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.CapturedAmount).HasColumnType("decimal(18,2)");
        builder.Property(x => x.PayPalFee).HasColumnType("decimal(18,2)");
        builder.Property(x => x.NetProceeds).HasColumnType("decimal(18,2)");
        builder.Property(x => x.PayPalOrderId).HasMaxLength(64);
        builder.Property(x => x.PayPalAuthorizationId).HasMaxLength(64);
        builder.Property(x => x.PayPalAuthorizationStatus).HasMaxLength(32);
        builder.Property(x => x.PayPalCaptureId).HasMaxLength(64);
        builder.Property(x => x.PayPalCaptureStatus).HasMaxLength(32);

        builder.Ignore(x => x.RefundedAmount);
        builder.Ignore(x => x.RefundableAmount);

        var navigation = builder.Metadata.FindNavigation(nameof(OrderPayment.Refunds));
        navigation?.SetPropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(x => x.Refunds).WithOne().HasForeignKey("OrderPaymentId").IsRequired().OnDelete(DeleteBehavior.Cascade);
    }
}

public class PaymentRefundConfiguration : IEntityTypeConfiguration<PaymentRefund>
{
    public void Configure(EntityTypeBuilder<PaymentRefund> builder)
    {
        builder.Property(x => x.IdempotencyKey).IsRequired().HasMaxLength(200);
        builder.HasIndex("OrderPaymentId", nameof(PaymentRefund.IdempotencyKey)).IsUnique();
        builder.Property(x => x.PayPalRefundId).IsRequired().HasMaxLength(64);
        builder.Property(x => x.Status).IsRequired().HasMaxLength(32);
        builder.Property(x => x.Amount).HasColumnType("decimal(18,2)");
    }
}
