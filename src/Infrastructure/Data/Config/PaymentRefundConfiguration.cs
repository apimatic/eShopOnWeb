using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentRefundConfiguration : IEntityTypeConfiguration<PaymentRefund>
{
    public void Configure(EntityTypeBuilder<PaymentRefund> builder)
    {
        builder.Property(r => r.IdempotencyKey).IsRequired().HasMaxLength(128);
        builder.Property(r => r.PayPalRefundId).IsRequired().HasMaxLength(64);
        builder.Property(r => r.Status).IsRequired().HasMaxLength(32);
        builder.Property(r => r.CurrencyCode).IsRequired().HasMaxLength(3);
        builder.Property(r => r.Amount).IsRequired().HasColumnType("decimal(18,2)");

        // The caller's idempotency key is unique per payment: replaying a refund request cannot
        // insert a second refund even if two requests race past the service-level check.
        builder.HasIndex("PaymentId", nameof(PaymentRefund.IdempotencyKey)).IsUnique();
    }
}
