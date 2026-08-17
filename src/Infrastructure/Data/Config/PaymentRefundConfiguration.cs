using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentRefundConfiguration : IEntityTypeConfiguration<PaymentRefund>
{
    public void Configure(EntityTypeBuilder<PaymentRefund> builder)
    {
        builder.Property(r => r.PayPalRefundId).IsRequired().HasMaxLength(64);
        builder.Property(r => r.Amount).HasColumnType("decimal(18,2)");
        builder.Property(r => r.Currency).IsRequired().HasMaxLength(3);
        builder.Property(r => r.Status).IsRequired().HasMaxLength(30);
        builder.Property(r => r.IdempotencyKey).IsRequired().HasMaxLength(128);
        builder.Property(r => r.NoteToPayer).HasMaxLength(255);

        // One refund per (payment, idempotency key): repeats collapse to the same row.
        builder.HasIndex(r => new { r.PaymentId, r.IdempotencyKey }).IsUnique();
    }
}
