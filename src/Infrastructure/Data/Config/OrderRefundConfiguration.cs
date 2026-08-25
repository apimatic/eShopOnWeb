using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderRefundConfiguration : IEntityTypeConfiguration<OrderRefund>
{
    public void Configure(EntityTypeBuilder<OrderRefund> builder)
    {
        builder.Property(r => r.RefundId).IsRequired().HasMaxLength(100);
        builder.Property(r => r.IdempotencyKey).IsRequired().HasMaxLength(200);
        builder.Property(r => r.Status).IsRequired().HasMaxLength(50);
        builder.Property(r => r.Amount).HasColumnType("decimal(18,2)");
    }
}
