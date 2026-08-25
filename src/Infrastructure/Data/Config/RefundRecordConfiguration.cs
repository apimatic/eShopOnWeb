using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class RefundRecordConfiguration : IEntityTypeConfiguration<RefundRecord>
{
    public void Configure(EntityTypeBuilder<RefundRecord> builder)
    {
        builder.Property(r => r.RefundId)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(r => r.IdempotencyKey)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(r => r.Amount)
            .HasColumnType("decimal(18,2)");

        builder.Property(r => r.Status)
            .IsRequired()
            .HasMaxLength(32);

        builder.HasOne<PaymentInfo>()
            .WithMany(p => p.Refunds)
            .HasForeignKey(r => r.PaymentInfoId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
