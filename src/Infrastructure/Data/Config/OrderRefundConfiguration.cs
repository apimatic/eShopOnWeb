using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderRefundConfiguration : IEntityTypeConfiguration<OrderRefund>
{
    public void Configure(EntityTypeBuilder<OrderRefund> builder)
    {
        builder.HasKey(refund => refund.Id);

        builder.Property(refund => refund.IdempotencyKey)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(refund => refund.FinixRefundTransferId)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(refund => refund.FinixRefundState)
            .HasMaxLength(50);

        builder.HasIndex(refund => new { refund.OrderId, refund.IdempotencyKey })
            .IsUnique();
    }
}
