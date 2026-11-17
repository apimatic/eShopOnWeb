using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class RefundRecordConfiguration : IEntityTypeConfiguration<RefundRecord>
{
    public void Configure(EntityTypeBuilder<RefundRecord> builder)
    {
        builder.Property(r => r.IdempotencyKey)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(r => r.FinixRefundTransferId)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(r => r.FinixState)
            .HasMaxLength(32);

        builder.HasIndex(r => new { r.OrderId, r.IdempotencyKey })
            .IsUnique();

        builder.HasIndex(r => r.FinixRefundTransferId)
            .IsUnique();
    }
}
