using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderRefundConfiguration : IEntityTypeConfiguration<OrderRefund>
{
    public void Configure(EntityTypeBuilder<OrderRefund> builder)
    {
        builder.Property(r => r.PayPalRefundId).IsRequired().HasMaxLength(100);
        builder.Property(r => r.CallerIdempotencyKey).IsRequired().HasMaxLength(500);
        builder.Property(r => r.RefundStatus).IsRequired().HasMaxLength(50);
        builder.Property(r => r.AmountValue).IsRequired().HasMaxLength(20);
        builder.Property(r => r.AmountCurrency).IsRequired().HasMaxLength(10);

        builder.HasIndex(r => new { r.PaymentId, r.CallerIdempotencyKey }).IsUnique();
    }
}
