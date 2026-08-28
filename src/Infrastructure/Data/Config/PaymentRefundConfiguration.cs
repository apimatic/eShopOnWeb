using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentRefundConfiguration : IEntityTypeConfiguration<PaymentRefund>
{
    public void Configure(EntityTypeBuilder<PaymentRefund> builder)
    {
        builder.ToTable("PaymentRefunds");
        builder.Property(x => x.PayPalRefundId).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Amount).HasColumnType("decimal(18,2)");
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.IdempotencyKey).HasMaxLength(128).IsRequired();
        builder.HasIndex(x => x.PayPalRefundId).IsUnique();
        builder.HasIndex(x => new { x.OrderPaymentId, x.IdempotencyKey }).IsUnique();
    }
}
