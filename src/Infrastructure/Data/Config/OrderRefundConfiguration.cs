using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderRefundConfiguration : IEntityTypeConfiguration<OrderRefund>
{
    public void Configure(EntityTypeBuilder<OrderRefund> builder)
    {
        builder.Property(r => r.IdempotencyKey).IsRequired().HasMaxLength(256);
        builder.Property(r => r.PayPalRefundId).HasMaxLength(256);
        builder.Property(r => r.Amount).HasPrecision(18, 2);
    }
}
