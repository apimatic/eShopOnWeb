using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class RefundRecordConfiguration : IEntityTypeConfiguration<RefundRecord>
{
    public void Configure(EntityTypeBuilder<RefundRecord> builder)
    {
        builder.HasKey(r => r.Id);
        builder.HasIndex(r => new { r.OrderPaymentId, r.IdempotencyKey }).IsUnique();
        builder.HasIndex(r => r.FinixTransferId);

        builder.Property(r => r.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(r => r.IdempotencyKey).IsRequired().HasMaxLength(200);
        builder.Property(r => r.FinixTransferId).HasMaxLength(64);
        builder.Property(r => r.FinixState).HasMaxLength(32);
        builder.Property(r => r.Status).HasMaxLength(32);
        builder.Property(r => r.FailureCode).HasMaxLength(64);
        builder.Property(r => r.FailureMessage).HasMaxLength(1000);
    }
}
