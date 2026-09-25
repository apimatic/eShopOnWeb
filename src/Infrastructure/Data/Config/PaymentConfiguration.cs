using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.Property(p => p.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(p => p.CurrencyCode).IsRequired().HasMaxLength(3);
        builder.Property(p => p.PaymentMethodKind).IsRequired().HasMaxLength(20);
        builder.Property(p => p.PaymentMethodDescription).HasMaxLength(100);

        builder.Property(p => p.AuthorizedAmount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.CapturedAmount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.PayPalFee).HasColumnType("decimal(18,2)");
        builder.Property(p => p.NetAmount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.RefundedAmount).HasColumnType("decimal(18,2)");

        builder.Property(p => p.PayPalOrderId).HasMaxLength(64);
        builder.Property(p => p.AuthorizationId).HasMaxLength(64);
        builder.Property(p => p.AuthorizationStatus).HasMaxLength(40);
        builder.Property(p => p.CaptureId).HasMaxLength(64);
        builder.Property(p => p.CaptureStatus).HasMaxLength(40);

        // One payment per order.
        builder.HasIndex(p => p.OrderId).IsUnique();

        // Refunds are part of the Payment aggregate (owned). The unique index on the caller-supplied
        // idempotency key is the durable claim that rejects a duplicate refund (enforced on SQL
        // Server; the in-memory provider does not enforce it — see plan PRODUCTION READINESS #9).
        builder.OwnsMany(p => p.Refunds, r =>
        {
            r.WithOwner().HasForeignKey("PaymentId");
            r.Property(x => x.RefundId).IsRequired().HasMaxLength(64);
            r.Property(x => x.Status).IsRequired().HasMaxLength(40);
            r.Property(x => x.IdempotencyKey).IsRequired().HasMaxLength(128);
            r.Property(x => x.Amount).HasColumnType("decimal(18,2)");
            r.HasIndex("PaymentId", nameof(Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate.PaymentRefund.IdempotencyKey))
                .IsUnique();
        });
        builder.Navigation(p => p.Refunds).Metadata.SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
