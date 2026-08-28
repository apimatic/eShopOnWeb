using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentRefundConfiguration : IEntityTypeConfiguration<PaymentRefund>
{
    public void Configure(EntityTypeBuilder<PaymentRefund> builder)
    {
        builder.Property(x => x.Amount).HasColumnType("decimal(18,2)");
        builder.Property(x => x.IdempotencyKey).IsRequired().HasMaxLength(128);
        builder.Property(x => x.Currency).IsRequired().HasMaxLength(3);
        builder.Property(x => x.PayPalRefundId).HasMaxLength(64);
        builder.HasIndex("OrderId", nameof(PaymentRefund.IdempotencyKey)).IsUnique();
    }
}
