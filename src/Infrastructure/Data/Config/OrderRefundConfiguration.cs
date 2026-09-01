using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderRefundConfiguration : IEntityTypeConfiguration<OrderRefund>
{
    public void Configure(EntityTypeBuilder<OrderRefund> builder)
    {
        builder.Property(r => r.PayPalRefundId)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(r => r.Amount)
            .IsRequired()
            .HasColumnType("decimal(18,2)");

        builder.Property(r => r.Currency)
            .IsRequired()
            .HasMaxLength(3);

        builder.Property(r => r.Status)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(r => r.IdempotencyKey)
            .IsRequired()
            .HasMaxLength(128);

        builder.HasIndex(r => r.IdempotencyKey);
    }
}
