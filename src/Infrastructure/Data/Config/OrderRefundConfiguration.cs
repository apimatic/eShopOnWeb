using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderRefundConfiguration : IEntityTypeConfiguration<OrderRefund>
{
    public void Configure(EntityTypeBuilder<OrderRefund> builder)
    {
        builder.Property(r => r.FinixTransferId).HasMaxLength(64);
        builder.Property(r => r.IdempotencyKey)
            .IsRequired()
            .HasMaxLength(255);
        builder.Property(r => r.State)
            .IsRequired()
            .HasMaxLength(32);
        builder.Property(r => r.Currency)
            .IsRequired()
            .HasMaxLength(8);

        builder.HasIndex(r => r.OrderId);
        builder.HasIndex(r => new { r.OrderId, r.IdempotencyKey }).IsUnique();
    }
}
