using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentRefundConfiguration : IEntityTypeConfiguration<PaymentRefund>
{
    public void Configure(EntityTypeBuilder<PaymentRefund> builder)
    {
        builder.ToTable("PaymentRefunds");

        builder.Property(r => r.Amount).HasColumnType("decimal(18,2)");

        builder.Property(r => r.Currency)
            .IsRequired()
            .HasMaxLength(3);

        builder.Property(r => r.IdempotencyKey)
            .IsRequired()
            .HasMaxLength(128);

        // One refund per idempotency key per payment: a replayed request can never refund twice.
        builder.HasIndex(r => new { r.PaymentId, r.IdempotencyKey })
            .IsUnique();

        builder.Property(r => r.PayPalRefundId).HasMaxLength(64);
        builder.Property(r => r.NoteToPayer).HasMaxLength(255);

        builder.Property(r => r.Status)
            .HasConversion<string>()
            .HasMaxLength(32);
    }
}
