using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class RefundConfiguration : IEntityTypeConfiguration<Refund>
{
    public void Configure(EntityTypeBuilder<Refund> builder)
    {
        builder.ToTable("Refunds");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.FinixRefundId).HasMaxLength(255).IsRequired();
        builder.Property(r => r.ParentTransferId).HasMaxLength(255).IsRequired();
        builder.Property(r => r.State).HasMaxLength(64);
        builder.Property(r => r.IdempotencyKey).HasMaxLength(255).IsRequired();

        builder.HasIndex(r => new { r.PaymentId, r.IdempotencyKey }).IsUnique();
        builder.HasIndex(r => r.FinixRefundId);
    }
}
