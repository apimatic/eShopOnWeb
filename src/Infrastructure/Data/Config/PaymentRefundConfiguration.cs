using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentRefundConfiguration : IEntityTypeConfiguration<PaymentRefund>
{
    public void Configure(EntityTypeBuilder<PaymentRefund> builder)
    {
        builder.HasIndex(r => r.IdempotencyKey).IsUnique();

        builder.Property(r => r.FinixTransferId)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(r => r.IdempotencyKey)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(r => r.State)
            .IsRequired()
            .HasMaxLength(32);
    }
}
