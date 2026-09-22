using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderRefundConfiguration : IEntityTypeConfiguration<OrderRefund>
{
    public void Configure(EntityTypeBuilder<OrderRefund> builder)
    {
        builder.Property(r => r.IdempotencyKey).IsRequired().HasMaxLength(128);

        // The refund idempotency claim is the PRIMARY KEY (OrderId, IdempotencyKey): a repeat under the
        // same key is rejected, while two distinct keys are two legitimate partial refunds. Using the
        // primary key (not an alternate key or unique index) is deliberate — the in-memory provider this
        // app runs on enforces primary-key uniqueness but NOT alternate keys or unique indexes.
        builder.HasKey(r => new { r.OrderId, r.IdempotencyKey });

        // Id remains the public refundId returned to the caller (a plain, non-key column).
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.HasIndex(r => r.Id);

        builder.Property(r => r.Currency).IsRequired().HasMaxLength(3);
        builder.Property(r => r.Status).IsRequired().HasMaxLength(32);
        builder.Property(r => r.PayPalRefundId).HasMaxLength(64);
        builder.Property(r => r.Amount).HasColumnType("decimal(18,4)");
    }
}
