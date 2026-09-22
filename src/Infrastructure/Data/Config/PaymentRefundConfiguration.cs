using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentRefundConfiguration : IEntityTypeConfiguration<PaymentRefund>
{
    public void Configure(EntityTypeBuilder<PaymentRefund> builder)
    {
        builder.Property(r => r.IdempotencyKey).IsRequired().HasMaxLength(200);
        builder.Property(r => r.Status).IsRequired().HasMaxLength(32);
        builder.Property(r => r.PayPalRefundId).HasMaxLength(64);
        builder.Property(r => r.Amount).HasColumnType("decimal(18,2)");

        // A repeat under the same idempotency key for the same payment must not create a second refund row.
        builder.HasIndex("OrderPaymentId", nameof(PaymentRefund.IdempotencyKey)).IsUnique();
    }
}
