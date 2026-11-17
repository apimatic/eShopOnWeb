using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class RefundRecordConfiguration : IEntityTypeConfiguration<RefundRecord>
{
    public void Configure(EntityTypeBuilder<RefundRecord> builder)
    {
        builder.HasKey(r => new { r.OrderId, r.IdempotencyKey });
        builder.Property(r => r.IdempotencyKey).HasMaxLength(128);
        builder.Property(r => r.RefundId).HasMaxLength(64);
        builder.Property(r => r.CaptureTransferId).HasMaxLength(64);
        builder.Property(r => r.State).HasMaxLength(32);
        builder.HasIndex(r => r.OrderId);
    }
}
