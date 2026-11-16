using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentRefundConfiguration : IEntityTypeConfiguration<PaymentRefund>
{
    public void Configure(EntityTypeBuilder<PaymentRefund> builder)
    {
        builder.HasKey(r => r.Id);
        builder.Property(r => r.BuyerId).HasMaxLength(256).IsRequired();
        builder.Property(r => r.RefundTransferId).HasMaxLength(64).IsRequired();
        builder.Property(r => r.IdempotencyKey).HasMaxLength(128).IsRequired();
        builder.Property(r => r.State).HasMaxLength(32);
        builder.HasIndex(r => r.IdempotencyKey).IsUnique();
        builder.HasIndex(r => r.OrderId);
    }
}
