using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentRefundConfiguration : IEntityTypeConfiguration<PaymentRefund>
{
    public void Configure(EntityTypeBuilder<PaymentRefund> builder)
    {
        builder.ToTable("PaymentRefunds");

        builder.Property(refund => refund.RefundId)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(refund => refund.IdempotencyKey)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(refund => refund.Status)
            .HasMaxLength(32);

        builder.HasIndex(refund => refund.RefundId)
            .IsUnique();

        builder.HasIndex(refund => new { refund.OrderId, refund.IdempotencyKey })
            .IsUnique();
    }
}
