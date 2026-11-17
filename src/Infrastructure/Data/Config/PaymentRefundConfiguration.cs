using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentRefundConfiguration : IEntityTypeConfiguration<PaymentRefund>
{
    public void Configure(EntityTypeBuilder<PaymentRefund> builder)
    {
        builder.Property(r => r.FinixTransferId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(r => r.FinixParentTransferId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(r => r.Currency)
            .IsRequired()
            .HasMaxLength(3);

        builder.Property(r => r.IdempotencyKey)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(r => r.State)
            .HasMaxLength(64);

        builder.HasIndex(r => new { r.OrderId, r.IdempotencyKey }).IsUnique();
    }
}
