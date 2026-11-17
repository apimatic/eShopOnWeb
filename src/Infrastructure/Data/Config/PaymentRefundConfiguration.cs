using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentRefundConfiguration : IEntityTypeConfiguration<PaymentRefund>
{
    public void Configure(EntityTypeBuilder<PaymentRefund> builder)
    {
        builder.ToTable("PaymentRefunds");

        builder.Property(r => r.FinixRefundId).HasMaxLength(64);
        builder.Property(r => r.Status).IsRequired().HasMaxLength(32);
        builder.Property(r => r.IdempotencyKey).IsRequired().HasMaxLength(128);
        builder.Property(r => r.FailureCode).HasMaxLength(64);
        builder.Property(r => r.FailureMessage).HasMaxLength(1000);

        builder.HasIndex(r => r.OrderPaymentId);
        builder.HasIndex(r => r.FinixRefundId);
        builder.HasIndex(r => new { r.OrderPaymentId, r.IdempotencyKey }).IsUnique();
    }
}
