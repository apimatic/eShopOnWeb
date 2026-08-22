using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentRefundConfiguration : IEntityTypeConfiguration<PaymentRefund>
{
    public void Configure(EntityTypeBuilder<PaymentRefund> builder)
    {
        builder.ToTable("OrderRefunds");

        builder.Property(r => r.PayPalRefundId)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(r => r.PayPalRefundStatus)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(r => r.IdempotencyKey)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(r => r.Amount)
            .HasColumnType("decimal(18,2)")
            .IsRequired();

        builder.Property(r => r.Currency)
            .IsRequired()
            .HasMaxLength(3);

        builder.HasIndex(r => new { r.OrderId, r.IdempotencyKey })
            .IsUnique();
    }
}
