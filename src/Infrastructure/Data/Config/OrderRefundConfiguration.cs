using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderRefundConfiguration : IEntityTypeConfiguration<OrderRefund>
{
    public void Configure(EntityTypeBuilder<OrderRefund> builder)
    {
        // A repeated refund request carrying the same caller idempotency key must not refund twice — the
        // unique index rejects the second insert and the code catches DbUpdateException.
        builder.HasIndex(r => r.IdempotencyKey).IsUnique();

        builder.Property(r => r.IdempotencyKey).IsRequired().HasMaxLength(127);
        builder.Property(r => r.Currency).IsRequired().HasMaxLength(3);
        builder.Property(r => r.Status).IsRequired().HasMaxLength(30);
        builder.Property(r => r.Amount).HasColumnType("decimal(18,2)");
    }
}
