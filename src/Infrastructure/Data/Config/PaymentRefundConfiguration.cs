using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentRefundConfiguration : IEntityTypeConfiguration<PaymentRefund>
{
    public void Configure(EntityTypeBuilder<PaymentRefund> builder)
    {
        builder.Property(x => x.IdempotencyKey).IsRequired().HasMaxLength(108);
        builder.Property(x => x.PayPalRefundId).IsRequired().HasMaxLength(36);
        builder.Property(x => x.Status).IsRequired().HasMaxLength(32);
        builder.Property(x => x.Amount).IsRequired().HasColumnType("decimal(18,2)");
        builder.HasIndex(x => new { x.OrderId, x.IdempotencyKey }).IsUnique();
        builder.HasIndex(x => x.PayPalRefundId).IsUnique();
    }
}
