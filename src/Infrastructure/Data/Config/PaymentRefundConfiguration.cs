using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentRefundConfiguration : IEntityTypeConfiguration<PaymentRefund>
{
    public void Configure(EntityTypeBuilder<PaymentRefund> builder)
    {
        // One refund per idempotency key per payment: a repeated request never refunds twice.
        builder.HasIndex(r => new { r.OrderPaymentId, r.IdempotencyKey }).IsUnique();

        builder.Property(r => r.IdempotencyKey)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(r => r.PayPalRefundId)
            .HasMaxLength(64);

        builder.Property(r => r.Status)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(r => r.Amount).HasColumnType("decimal(18,2)");

        builder.Property(r => r.Currency)
            .IsRequired()
            .HasMaxLength(3);

        builder.Property(r => r.Note).HasMaxLength(512);
    }
}
