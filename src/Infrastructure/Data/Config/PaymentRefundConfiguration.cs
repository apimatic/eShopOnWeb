using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentRefundConfiguration : IEntityTypeConfiguration<PaymentRefund>
{
    public void Configure(EntityTypeBuilder<PaymentRefund> builder)
    {
        builder.Property(r => r.PayPalRefundId)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(r => r.PayPalStatus)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(r => r.Amount).HasColumnType("decimal(18,2)");

        builder.Property(r => r.IdempotencyKey)
            .IsRequired()
            .HasMaxLength(127);

        builder.Property(r => r.NoteToPayer).HasMaxLength(255);

        builder.HasIndex(r => r.IdempotencyKey);
    }
}
