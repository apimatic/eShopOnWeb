using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentRefundConfiguration : IEntityTypeConfiguration<PaymentRefund>
{
    public void Configure(EntityTypeBuilder<PaymentRefund> builder)
    {
        builder.HasIndex(x => new { x.OrderPaymentId, x.IdempotencyKey }).IsUnique();
        builder.HasIndex(x => x.PayPalRefundId).IsUnique().HasFilter("[PayPalRefundId] IS NOT NULL");
        builder.Property(x => x.IdempotencyKey).HasMaxLength(200).IsRequired();
        builder.Property(x => x.PayPalRequestId).HasMaxLength(108).IsRequired();
        builder.Property(x => x.PayPalRefundId).HasMaxLength(64);
        builder.Property(x => x.PayPalStatus).HasMaxLength(32);
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.Amount).HasColumnType("decimal(18,2)");
    }
}
