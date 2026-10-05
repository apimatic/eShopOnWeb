using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderPaymentConfiguration : IEntityTypeConfiguration<OrderPayment>
{
    public void Configure(EntityTypeBuilder<OrderPayment> builder)
    {
        builder.HasIndex(p => p.OrderId).IsUnique();
        builder.Property(p => p.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(p => p.Amount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.Currency).IsRequired().HasMaxLength(3);
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(p => p.LastError).HasMaxLength(1000);
        builder.Property(p => p.InvoiceId).HasMaxLength(127);
        builder.Property(p => p.CreateOrderRequestId).HasMaxLength(108);
        builder.Property(p => p.AuthorizeRequestId).HasMaxLength(108);
        builder.Property(p => p.ReauthorizeRequestId).HasMaxLength(108);
        builder.Property(p => p.CaptureRequestId).HasMaxLength(108);
        builder.Property(p => p.VoidRequestId).HasMaxLength(108);
        builder.Property(p => p.PayPalOrderId).HasMaxLength(64);
        builder.Property(p => p.AuthorizationId).HasMaxLength(64);
        builder.Property(p => p.AuthorizationStatus).HasMaxLength(32);
        builder.Property(p => p.CaptureId).HasMaxLength(64);
        builder.Property(p => p.CaptureStatus).HasMaxLength(32);
        builder.Property(p => p.CardBrand).HasMaxLength(32);
        builder.Property(p => p.CardLastDigits).HasMaxLength(4);
        builder.Property(p => p.CapturedAmount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.PayPalFee).HasColumnType("decimal(18,2)");
        builder.Property(p => p.NetAmount).HasColumnType("decimal(18,2)");
        builder.Ignore(p => p.IsTransitional);
        builder.Ignore(p => p.RefundedAmount);
        builder.Ignore(p => p.RefundableAmount);

        builder.HasMany(p => p.Refunds).WithOne().HasForeignKey(r => r.OrderPaymentId);
        builder.Metadata.FindNavigation(nameof(OrderPayment.Refunds))?.SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}

public class PaymentRefundConfiguration : IEntityTypeConfiguration<PaymentRefund>
{
    public void Configure(EntityTypeBuilder<PaymentRefund> builder)
    {
        builder.HasIndex(r => new { r.OrderPaymentId, r.IdempotencyKey }).IsUnique();
        builder.Property(r => r.IdempotencyKey).IsRequired().HasMaxLength(64);
        builder.Property(r => r.Amount).HasColumnType("decimal(18,2)");
        builder.Property(r => r.PayPalRequestId).IsRequired().HasMaxLength(108);
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(r => r.PayPalRefundId).HasMaxLength(64);
        builder.Property(r => r.PayPalStatus).HasMaxLength(32);
        builder.Property(r => r.FailureReason).HasMaxLength(1000);
        builder.Ignore(r => r.ReservesFunds);
    }
}

public class PaymentClaimConfiguration : IEntityTypeConfiguration<PaymentClaim>
{
    public void Configure(EntityTypeBuilder<PaymentClaim> builder)
    {
        // The key IS the claim: the primary key makes the store refuse a second claim.
        builder.HasKey(c => c.Key);
        builder.Property(c => c.Key).HasMaxLength(200);
    }
}
