using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderRefundConfiguration : IEntityTypeConfiguration<OrderRefund>
{
    public void Configure(EntityTypeBuilder<OrderRefund> builder)
    {
        builder.ToTable("OrderRefunds");
        builder.Property(r => r.RefundId).HasMaxLength(64).IsRequired();
        builder.Property(r => r.IdempotencyKey).HasMaxLength(128).IsRequired();
        builder.Property(r => r.Status).HasMaxLength(64).IsRequired();
        builder.Property(r => r.Amount).HasColumnType("decimal(18,2)");
        builder.HasIndex(r => new { r.OrderPaymentId, r.IdempotencyKey }).IsUnique();
    }
}
