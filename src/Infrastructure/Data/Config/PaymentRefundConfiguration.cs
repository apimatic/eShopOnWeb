using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentRefundConfiguration : IEntityTypeConfiguration<PaymentRefund>
{
    public void Configure(EntityTypeBuilder<PaymentRefund> builder)
    {
        builder.Property(r => r.Amount).HasColumnType("decimal(18,2)");
        builder.Property(r => r.PayPalRefundId).IsRequired().HasMaxLength(64);
        builder.Property(r => r.Status).IsRequired().HasMaxLength(32);
        builder.Property(r => r.IdempotencyKey).IsRequired().HasMaxLength(128);

        // Two distinct partial refunds are legitimate, but the same idempotency key must never
        // produce two refunds against the same payment. "PaymentId" is the shadow foreign key.
        builder.HasIndex("PaymentId", nameof(PaymentRefund.IdempotencyKey)).IsUnique();
    }
}
