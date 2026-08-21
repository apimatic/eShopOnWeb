using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentRefundConfiguration : IEntityTypeConfiguration<PaymentRefund>
{
    public void Configure(EntityTypeBuilder<PaymentRefund> builder)
    {
        builder.Property(r => r.PayPalRefundId).IsRequired().HasMaxLength(64);
        builder.Property(r => r.Status).HasMaxLength(40);
        builder.Property(r => r.IdempotencyKey).IsRequired().HasMaxLength(256);
        builder.Property(r => r.Amount).HasColumnType("decimal(18,2)");
    }
}
