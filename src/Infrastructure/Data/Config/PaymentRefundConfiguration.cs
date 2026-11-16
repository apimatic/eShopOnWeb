using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentRefundConfiguration : IEntityTypeConfiguration<PaymentRefund>
{
    public void Configure(EntityTypeBuilder<PaymentRefund> builder)
    {
        builder.ToTable("PaymentRefunds");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.FinixRefundTransferId)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(r => r.State)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(r => r.IdempotencyKey)
            .IsRequired()
            .HasMaxLength(128);

        builder.HasIndex(r => r.PaymentId);
        builder.HasIndex(r => r.FinixRefundTransferId);
        builder.HasIndex(r => new { r.OrderId, r.IdempotencyKey }).IsUnique();
    }
}
