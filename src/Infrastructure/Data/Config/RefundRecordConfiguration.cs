using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class RefundRecordConfiguration : IEntityTypeConfiguration<RefundRecord>
{
    public void Configure(EntityTypeBuilder<RefundRecord> builder)
    {
        builder.HasKey(r => r.ClaimKey);

        builder.Property(r => r.ClaimKey)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(r => r.IdempotencyKey)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(r => r.FinixRefundTransferId).HasMaxLength(64);
        builder.Property(r => r.State).HasMaxLength(32);
        builder.Property(r => r.FailureCode).HasMaxLength(64);
        builder.Property(r => r.FailureMessage).HasMaxLength(500);

        builder.HasIndex(r => r.OrderId);
    }
}
