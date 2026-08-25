using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentRefundConfiguration : IEntityTypeConfiguration<PaymentRefund>
{
    public void Configure(EntityTypeBuilder<PaymentRefund> builder)
    {
        builder.ToTable("PaymentRefunds");
        builder.Property(r => r.PayPalRefundId).HasMaxLength(50).IsRequired();
        builder.Property(r => r.Currency).HasMaxLength(3).IsRequired();
        builder.Property(r => r.IdempotencyKey).HasMaxLength(200).IsRequired();
        builder.Property(r => r.Amount).HasColumnType("decimal(18,2)");
        builder.HasIndex(r => r.IdempotencyKey).IsUnique();
    }
}
