using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderRefundConfiguration : IEntityTypeConfiguration<OrderRefund>
{
    public void Configure(EntityTypeBuilder<OrderRefund> builder)
    {
        builder.HasKey(x => new { x.OrderId, x.IdempotencyKey });
        builder.Property(x => x.IdempotencyKey).IsRequired().HasMaxLength(200);
        builder.Property(x => x.RefundId).IsRequired().HasMaxLength(64);
        builder.Property(x => x.FinixReversalId).HasMaxLength(64);
        builder.Property(x => x.State).HasMaxLength(32);
        builder.HasIndex(x => x.FinixReversalId);
    }
}
