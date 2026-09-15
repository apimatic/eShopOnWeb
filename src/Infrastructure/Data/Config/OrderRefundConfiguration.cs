using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderRefundConfiguration : IEntityTypeConfiguration<OrderRefund>
{
    public void Configure(EntityTypeBuilder<OrderRefund> builder)
    {
        builder.Property(x => x.IdempotencyKey).IsRequired().HasMaxLength(128);
        builder.Property(x => x.PayPalRefundId).IsRequired().HasMaxLength(64);
        builder.Property(x => x.PayPalStatus).IsRequired().HasMaxLength(32);
        builder.Property(x => x.Amount).HasColumnType("decimal(18,2)");
        builder.HasIndex(x => new { x.OrderId, x.IdempotencyKey }).IsUnique();
        builder.HasIndex(x => x.PayPalRefundId).IsUnique();
    }
}
