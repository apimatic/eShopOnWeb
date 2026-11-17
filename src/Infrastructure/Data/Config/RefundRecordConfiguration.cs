using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class RefundRecordConfiguration : IEntityTypeConfiguration<RefundRecord>
{
    public void Configure(EntityTypeBuilder<RefundRecord> builder)
    {
        builder.ToTable("RefundRecords");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.IdempotencyKey)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(r => r.FinixRefundId).HasMaxLength(64);
        builder.Property(r => r.State).IsRequired().HasMaxLength(32);
        builder.Property(r => r.FailureMessage).HasMaxLength(1000);

        builder.HasIndex(r => new { r.OrderPaymentId, r.IdempotencyKey }).IsUnique();
    }
}
