using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public sealed class OrderRefundConfiguration : IEntityTypeConfiguration<OrderRefund>
{
    public void Configure(EntityTypeBuilder<OrderRefund> builder)
    {
        builder.Property(r => r.IdempotencyKey).HasMaxLength(128).IsRequired();
        builder.Property(r => r.ProviderRefundId).HasMaxLength(64).IsRequired();
        builder.Property(r => r.Status).HasMaxLength(32).IsRequired();
        builder.Property(r => r.Amount).HasColumnType("decimal(18,2)").IsRequired();
        builder.HasIndex("OrderId", nameof(OrderRefund.IdempotencyKey)).IsUnique();
        builder.HasIndex(r => r.ProviderRefundId).IsUnique();
    }
}
