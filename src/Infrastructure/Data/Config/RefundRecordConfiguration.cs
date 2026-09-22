using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class RefundRecordConfiguration : IEntityTypeConfiguration<RefundRecord>
{
    public void Configure(EntityTypeBuilder<RefundRecord> builder)
    {
        builder.Property(r => r.IdempotencyKey).IsRequired().HasMaxLength(100);
        builder.Property(r => r.PayPalRefundId).IsRequired().HasMaxLength(64);
        builder.Property(r => r.Amount).HasColumnType("decimal(18,2)");
        builder.Property(r => r.Status).HasMaxLength(30);

        // The caller-supplied idempotency key is unique — a repeated refund under the same key is rejected.
        builder.HasIndex(r => r.IdempotencyKey).IsUnique();
    }
}
