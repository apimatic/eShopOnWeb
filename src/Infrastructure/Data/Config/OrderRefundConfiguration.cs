using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderRefundConfiguration : IEntityTypeConfiguration<OrderRefund>
{
    public void Configure(EntityTypeBuilder<OrderRefund> builder)
    {
        builder.Property(r => r.FinixRefundId)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(r => r.State)
            .HasMaxLength(32);

        builder.Property(r => r.IdempotencyKey)
            .IsRequired()
            .HasMaxLength(256);

        builder.HasIndex(r => r.OrderId);
        builder.HasIndex(r => new { r.OrderId, r.IdempotencyKey }).IsUnique();
        builder.HasIndex(r => r.FinixRefundId).IsUnique();
    }
}
