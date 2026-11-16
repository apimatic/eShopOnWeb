using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class RefundConfiguration : IEntityTypeConfiguration<Refund>
{
    public void Configure(EntityTypeBuilder<Refund> builder)
    {
        builder.Property(r => r.IdempotencyKey)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(r => r.FinixRefundTransferId)
            .HasMaxLength(64);

        builder.Property(r => r.State)
            .HasMaxLength(32);

        builder.Property(r => r.FailureCode)
            .HasMaxLength(64);

        builder.Property(r => r.FailureMessage)
            .HasMaxLength(1000);

        builder.HasIndex(r => new { r.OrderPaymentId, r.IdempotencyKey }).IsUnique();
        builder.HasIndex(r => r.OrderId);
        builder.HasIndex(r => r.FinixRefundTransferId);
    }
}
