using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderRefundConfiguration : IEntityTypeConfiguration<OrderRefund>
{
    public void Configure(EntityTypeBuilder<OrderRefund> builder)
    {
        builder.Property(x => x.IdempotencyKey).IsRequired().HasMaxLength(78);
        builder.Property(x => x.PayPalRefundId).HasMaxLength(64);
        builder.Property(x => x.PayPalStatus).HasMaxLength(32);
        builder.Property(x => x.Amount).IsRequired().HasColumnType("decimal(18,2)");
        builder.Property(x => x.Currency).IsRequired().HasMaxLength(3);
        builder.Property(x => x.FailureReason).HasMaxLength(256);
        builder.HasIndex("OrderId", nameof(OrderRefund.IdempotencyKey)).IsUnique();
        builder.HasIndex(x => x.PayPalRefundId).IsUnique().HasFilter("[PayPalRefundId] IS NOT NULL");
    }
}
