using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class RefundConfiguration : IEntityTypeConfiguration<Refund>
{
    public void Configure(EntityTypeBuilder<Refund> builder)
    {
        builder.Property(r => r.PayPalRefundId).IsRequired().HasMaxLength(50);
        builder.Property(r => r.Status).IsRequired().HasMaxLength(30);
        builder.Property(r => r.IdempotencyKey).IsRequired().HasMaxLength(100);
        builder.Property(r => r.Amount).HasColumnType("decimal(18,2)");

        builder.HasIndex("PaymentId", nameof(Refund.IdempotencyKey)).IsUnique();
    }
}
