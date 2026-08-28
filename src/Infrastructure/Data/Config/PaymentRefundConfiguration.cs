using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public sealed class PaymentRefundConfiguration : IEntityTypeConfiguration<PaymentRefund>
{
    public void Configure(EntityTypeBuilder<PaymentRefund> builder)
    {
        builder.Property(refund => refund.PayPalRefundId).IsRequired().HasMaxLength(32);
        builder.Property(refund => refund.IdempotencyKey).IsRequired().HasMaxLength(200);
        builder.Property(refund => refund.Status).IsRequired().HasMaxLength(32);
        builder.Property(refund => refund.Amount).HasColumnType("decimal(18,2)");
        builder.HasIndex(refund => refund.PayPalRefundId).IsUnique();
        builder.HasIndex(refund => new { refund.OrderId, refund.IdempotencyKey }).IsUnique();
    }
}
