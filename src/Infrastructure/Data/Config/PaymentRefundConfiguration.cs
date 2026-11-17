using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentRefundConfiguration : IEntityTypeConfiguration<PaymentRefund>
{
    public void Configure(EntityTypeBuilder<PaymentRefund> builder)
    {
        builder.HasKey(r => r.Id);

        builder.Property(r => r.FinixReversalId)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(r => r.FinixState)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(r => r.IdempotencyKey)
            .IsRequired()
            .HasMaxLength(128);

        builder.HasIndex(r => new { r.OrderPaymentId, r.IdempotencyKey }).IsUnique();
    }
}
