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
            .HasMaxLength(200);

        builder.Property(r => r.FinixRefundTransferId).HasMaxLength(64);
        builder.Property(r => r.FinixState).HasMaxLength(32);

        builder.HasIndex(r => new { r.OrderPaymentId, r.IdempotencyKey }).IsUnique();
        builder.HasIndex(r => r.FinixRefundTransferId);
    }
}
