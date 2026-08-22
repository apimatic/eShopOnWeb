using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderRefundConfiguration : IEntityTypeConfiguration<OrderRefund>
{
    public void Configure(EntityTypeBuilder<OrderRefund> builder)
    {
        builder.ToTable("OrderRefunds");

        builder.Property(r => r.PayPalRefundId)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(r => r.Status)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(r => r.Amount)
            .HasColumnType("decimal(18,2)")
            .IsRequired();

        builder.Property(r => r.Currency)
            .IsRequired()
            .HasMaxLength(3);

        builder.Property(r => r.IdempotencyKey)
            .IsRequired()
            .HasMaxLength(128);

        builder.HasIndex(r => r.IdempotencyKey);
    }
}
