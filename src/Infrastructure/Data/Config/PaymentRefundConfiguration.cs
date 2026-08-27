using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentRefundConfiguration : IEntityTypeConfiguration<PaymentRefund>
{
    public void Configure(EntityTypeBuilder<PaymentRefund> builder)
    {
        builder.Property(r => r.PayPalRefundId).IsRequired().HasMaxLength(64);
        builder.Property(r => r.IdempotencyKey).IsRequired().HasMaxLength(128);
        builder.Property(r => r.Status).IsRequired().HasMaxLength(32);
        builder.Property(r => r.Amount).HasColumnType("decimal(18,2)");

        // A caller-supplied idempotency key may only ever produce one refund per payment.
        builder.HasIndex("OrderPaymentId", nameof(PaymentRefund.IdempotencyKey)).IsUnique();
    }
}
