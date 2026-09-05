using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderPaymentConfiguration : IEntityTypeConfiguration<OrderPayment>
{
    public void Configure(EntityTypeBuilder<OrderPayment> builder)
    {
        // One payment per order: the order is what money is held and taken against.
        builder.HasIndex(payment => payment.OrderId).IsUnique();

        builder.Property(payment => payment.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(payment => payment.Currency)
            .IsRequired()
            .HasMaxLength(3);

        builder.Property(payment => payment.Status)
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(payment => payment.Amount).HasColumnType("decimal(18,2)");
        builder.Property(payment => payment.CapturedAmount).HasColumnType("decimal(18,2)");
        builder.Property(payment => payment.FeeAmount).HasColumnType("decimal(18,2)");
        builder.Property(payment => payment.NetAmount).HasColumnType("decimal(18,2)");

        builder.Property(payment => payment.Reference)
            .IsRequired()
            .HasMaxLength(16);

        builder.Property(payment => payment.PayPalOrderId).HasMaxLength(64);
        builder.Property(payment => payment.AuthorizationId).HasMaxLength(64);
        builder.Property(payment => payment.RenewedFromAuthorizationId).HasMaxLength(64);
        builder.Property(payment => payment.CaptureId).HasMaxLength(64);
        builder.Property(payment => payment.CaptureStatus).HasMaxLength(32);
        builder.Property(payment => payment.CaptureRequestId).HasMaxLength(160);
        builder.Property(payment => payment.CardVaultId).HasMaxLength(64);
        builder.Property(payment => payment.PayPalCustomerId).HasMaxLength(64);
        builder.Property(payment => payment.AuthorizationStatus).HasMaxLength(32);

        // Refunds only ever exist as part of the payment they were taken against.
        builder.HasMany(payment => payment.Refunds)
            .WithOne()
            .HasForeignKey("OrderPaymentId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        var refundsNavigation = builder.Metadata.FindNavigation(nameof(OrderPayment.Refunds));
        refundsNavigation?.SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}

public class PaymentRefundConfiguration : IEntityTypeConfiguration<PaymentRefund>
{
    public void Configure(EntityTypeBuilder<PaymentRefund> refund)
    {
        refund.Property(r => r.IdempotencyKey)
            .IsRequired()
            .HasMaxLength(64);

        refund.Property(r => r.Currency)
            .IsRequired()
            .HasMaxLength(3);

        refund.Property(r => r.Status)
            .HasConversion<string>()
            .HasMaxLength(16);

        refund.Property(r => r.Amount).HasColumnType("decimal(18,2)");
        refund.Property(r => r.FeeReturned).HasColumnType("decimal(18,2)");
        refund.Property(r => r.NetAmount).HasColumnType("decimal(18,2)");
        refund.Property(r => r.PayPalRefundId).HasMaxLength(64);

        // A caller's idempotency key may be reused by different shoppers on different payments, so it
        // is unique per payment rather than absolutely.
        refund.HasIndex("OrderPaymentId", nameof(PaymentRefund.IdempotencyKey)).IsUnique();
    }
}
