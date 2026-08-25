using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderRefundConfiguration : IEntityTypeConfiguration<OrderRefund>
{
    public void Configure(EntityTypeBuilder<OrderRefund> builder)
    {
        builder.Property(r => r.Amount).HasColumnType("decimal(18,2)");
        builder.Property(r => r.IdempotencyKey).IsRequired().HasMaxLength(200);

        // Repeating a refund request under the same idempotency key must resolve to the same row.
        builder.HasIndex(r => new { r.OrderPaymentId, r.IdempotencyKey }).IsUnique();
    }
}
