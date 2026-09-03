using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public sealed class OrderRefundConfiguration : IEntityTypeConfiguration<OrderRefund>
{
    public void Configure(EntityTypeBuilder<OrderRefund> builder)
    {
        builder.Property<int>("OrderId");
        builder.Property(x => x.IdempotencyKey).IsRequired().HasMaxLength(108);
        builder.HasIndex("OrderId", nameof(OrderRefund.IdempotencyKey)).IsUnique();
        builder.Property(x => x.Amount).HasColumnType("decimal(18,2)");
        builder.Property(x => x.PayPalRefundId).HasMaxLength(64);
        builder.Property(x => x.PayPalStatus).HasMaxLength(32);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
    }
}
