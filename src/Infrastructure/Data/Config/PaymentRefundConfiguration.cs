using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentRefundConfiguration : IEntityTypeConfiguration<PaymentRefund>
{
    public void Configure(EntityTypeBuilder<PaymentRefund> builder)
    {
        builder.Property(x => x.PayPalRefundId).HasMaxLength(36).IsRequired();
        builder.HasIndex(x => x.PayPalRefundId).IsUnique();
        builder.Property(x => x.IdempotencyKey).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Amount).HasPrecision(18, 2);
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(32).IsRequired();
        builder.HasIndex("OrderId", nameof(PaymentRefund.IdempotencyKey)).IsUnique();
    }
}
