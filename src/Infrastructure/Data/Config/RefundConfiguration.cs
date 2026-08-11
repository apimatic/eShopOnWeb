using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class RefundConfiguration : IEntityTypeConfiguration<Refund>
{
    public void Configure(EntityTypeBuilder<Refund> builder)
    {
        builder.ToTable("Refunds");

        builder.Property(r => r.IdempotencyKey)
            .IsRequired()
            .HasMaxLength(128);

        // The same key can only book one refund against a given capture (idempotency).
        builder.HasIndex(r => new { r.PaymentId, r.IdempotencyKey }).IsUnique();

        builder.Property(r => r.Amount).HasColumnType("decimal(18,2)");
        builder.Property(r => r.PayPalRefundId).HasMaxLength(64);
        builder.Property(r => r.PayPalStatus).HasMaxLength(32);
    }
}
