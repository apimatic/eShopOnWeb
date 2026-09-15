using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentRefundConfiguration : IEntityTypeConfiguration<PaymentRefund>
{
    public void Configure(EntityTypeBuilder<PaymentRefund> builder)
    {
        builder.Property(x => x.IdempotencyKey).IsRequired().HasMaxLength(128);
        builder.Property(x => x.ProviderRefundId).HasMaxLength(128);
        builder.Property(x => x.ProviderStatus).IsRequired().HasMaxLength(32);
        builder.Property(x => x.Currency).IsRequired().HasMaxLength(3);
        builder.Property(x => x.Amount).HasPrecision(18, 2);
        builder.Property(x => x.ProviderCreateTime).HasMaxLength(64);
        builder.Property(x => x.ProviderUpdateTime).HasMaxLength(64);
        builder.HasIndex(x => new { x.OrderId, x.IdempotencyKey }).IsUnique();
        builder.HasIndex(x => x.ProviderRefundId).IsUnique().HasFilter("[ProviderRefundId] IS NOT NULL");
    }
}
