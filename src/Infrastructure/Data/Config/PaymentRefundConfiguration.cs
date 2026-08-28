using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentRefundConfiguration : IEntityTypeConfiguration<PaymentRefund>
{
    public void Configure(EntityTypeBuilder<PaymentRefund> builder)
    {
        builder.ToTable("PaymentRefunds");
        builder.HasIndex(x => new { x.OrderPaymentId, x.IdempotencyKey }).IsUnique();
        builder.HasIndex(x => x.PayPalRefundId).IsUnique();
        builder.Property(x => x.IdempotencyKey).HasMaxLength(108).IsRequired();
        builder.Property(x => x.PayPalRefundId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Amount).HasPrecision(18, 2);
    }
}
