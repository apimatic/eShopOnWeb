using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class RefundRecordConfiguration : IEntityTypeConfiguration<RefundRecord>
{
    public void Configure(EntityTypeBuilder<RefundRecord> builder)
    {
        builder.HasKey(r => r.Id);
        builder.Property(r => r.RefundId).IsRequired().HasMaxLength(64);
        builder.Property(r => r.IdempotencyKey).IsRequired().HasMaxLength(200);
        builder.Property(r => r.State).IsRequired().HasMaxLength(32);
        builder.HasIndex(r => r.OrderId);
        builder.HasIndex(r => new { r.OrderId, r.IdempotencyKey }).IsUnique();
    }
}
