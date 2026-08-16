using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentRefundConfiguration : IEntityTypeConfiguration<PaymentRefund>
{
    public void Configure(EntityTypeBuilder<PaymentRefund> builder)
    {
        builder.ToTable("PaymentRefunds");

        builder.Property(r => r.Amount).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(r => r.PayPalRefundId).HasMaxLength(64).IsRequired();
        builder.Property(r => r.Status).HasMaxLength(30).IsRequired();
        builder.Property(r => r.IdempotencyKey).HasMaxLength(128).IsRequired();
    }
}
