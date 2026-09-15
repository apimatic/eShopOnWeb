using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderRefundConfiguration : IEntityTypeConfiguration<OrderRefund>
{
    public void Configure(EntityTypeBuilder<OrderRefund> builder)
    {
        builder.ToTable("OrderRefunds");

        builder.Property(r => r.PayPalRefundId)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(r => r.Status)
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(r => r.Amount)
            .HasColumnType("decimal(18,2)")
            .IsRequired();

        builder.Property(r => r.Currency)
            .HasMaxLength(3)
            .IsRequired();

        builder.Property(r => r.IdempotencyKey)
            .HasMaxLength(108)
            .IsRequired();

        builder.HasIndex(r => new { r.OrderId, r.IdempotencyKey })
            .IsUnique();
    }
}
