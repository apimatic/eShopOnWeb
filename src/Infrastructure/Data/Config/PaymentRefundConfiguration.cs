using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentRefundConfiguration : IEntityTypeConfiguration<PaymentRefund>
{
    public void Configure(EntityTypeBuilder<PaymentRefund> builder)
    {
        builder.Property(r => r.Amount).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(r => r.Currency).HasMaxLength(3).IsRequired();
        builder.Property(r => r.IdempotencyKey).HasMaxLength(128).IsRequired();
        builder.Property(r => r.PayPalRequestId).HasMaxLength(128).IsRequired();
        builder.Property(r => r.PayPalRefundId).HasMaxLength(64);
        builder.Property(r => r.Status).HasMaxLength(32).IsRequired();
        builder.HasIndex(r => new { r.OrderId, r.IdempotencyKey }).IsUnique();
        builder.HasIndex(r => r.PayPalRefundId).IsUnique().HasFilter("[PayPalRefundId] IS NOT NULL");
    }
}
