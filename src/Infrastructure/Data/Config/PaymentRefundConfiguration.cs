using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentRefundConfiguration : IEntityTypeConfiguration<PaymentRefund>
{
    public void Configure(EntityTypeBuilder<PaymentRefund> builder)
    {
        builder.Property(r => r.IdempotencyKey)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(r => r.Currency)
            .IsRequired()
            .HasMaxLength(3);

        builder.Property(r => r.Amount).HasPrecision(18, 2);
        builder.Property(r => r.PayPalRefundId).HasMaxLength(64);
        builder.Property(r => r.State).HasConversion<int>();

        // Repeating a refund under the same idempotency key must not refund twice: the (payment, key)
        // pair is unique, so the second insert is rejected and caught. Two distinct keys (two partial
        // refunds) remain legitimate.
        builder.HasIndex(r => new { r.OrderPaymentId, r.IdempotencyKey }).IsUnique();
    }
}
