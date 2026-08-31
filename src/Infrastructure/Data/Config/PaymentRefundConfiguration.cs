using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentRefundConfiguration : IEntityTypeConfiguration<PaymentRefund>
{
    public void Configure(EntityTypeBuilder<PaymentRefund> builder)
    {
        builder.Property(x => x.CallerIdempotencyKey).HasMaxLength(200).IsRequired();
        builder.Property(x => x.PayPalRequestId).HasMaxLength(108).IsRequired();
        builder.Property(x => x.PayPalRefundId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Amount).HasColumnType("decimal(18,2)");
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.HasIndex(x => new { x.OrderId, x.CallerIdempotencyKey }).IsUnique();
        builder.HasIndex(x => x.PayPalRefundId).IsUnique();
    }
}
