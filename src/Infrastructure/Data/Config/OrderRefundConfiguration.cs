using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderRefundConfiguration : IEntityTypeConfiguration<OrderRefund>
{
    public void Configure(EntityTypeBuilder<OrderRefund> builder)
    {
        builder.Property(r => r.FinixTransferId).HasMaxLength(128).IsRequired();
        builder.Property(r => r.Currency).HasMaxLength(16).IsRequired();
        builder.Property(r => r.Status).HasMaxLength(64).IsRequired();
        builder.Property(r => r.IdempotencyKey).HasMaxLength(255).IsRequired();
        builder.Property(r => r.FailureCode).HasMaxLength(128);
        builder.Property(r => r.FailureMessage).HasMaxLength(1024);

        builder.HasIndex(r => new { r.OrderId, r.IdempotencyKey }).IsUnique();
        builder.HasIndex(r => r.FinixTransferId);
    }
}
