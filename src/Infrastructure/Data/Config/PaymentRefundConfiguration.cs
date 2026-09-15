using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentRefundConfiguration : IEntityTypeConfiguration<PaymentRefund>
{
    public void Configure(EntityTypeBuilder<PaymentRefund> builder)
    {
        builder.Property(r => r.RefundId).HasMaxLength(64).IsRequired();
        builder.Property(r => r.CurrencyCode).HasMaxLength(3).IsRequired();
        builder.Property(r => r.Amount).HasPrecision(18, 2);
        builder.Property(r => r.Status).HasMaxLength(30);
        builder.Property(r => r.IdempotencyKey).HasMaxLength(128).IsRequired();
    }
}
