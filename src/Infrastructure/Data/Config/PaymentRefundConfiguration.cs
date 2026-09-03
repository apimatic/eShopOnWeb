using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentRefundConfiguration : IEntityTypeConfiguration<PaymentRefund>
{
    public void Configure(EntityTypeBuilder<PaymentRefund> builder)
    {
        builder.Property(r => r.IdempotencyKey).HasMaxLength(200).IsRequired();
        builder.Property(r => r.ProviderRequestId).HasMaxLength(64).IsRequired();
        builder.Property(r => r.ProviderRefundId).HasMaxLength(64);
        builder.Property(r => r.Status).HasMaxLength(32).IsRequired();
        builder.Property(r => r.Amount).HasPrecision(18, 2);
        builder.HasIndex(r => new { r.OrderPaymentId, r.IdempotencyKey }).IsUnique();
        builder.HasIndex(r => r.ProviderRefundId);
        builder.HasIndex(r => r.UpdatedAt);
    }
}
