using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class RefundConfiguration : IEntityTypeConfiguration<Refund>
{
    public void Configure(EntityTypeBuilder<Refund> builder)
    {
        builder.Property(r => r.IdempotencyKey)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(r => r.Amount).HasColumnType("decimal(18,2)");
        builder.Property(r => r.CurrencyCode).IsRequired().HasMaxLength(3);
        builder.Property(r => r.Status).IsRequired().HasMaxLength(30);
        builder.Property(r => r.PayPalRefundId).HasMaxLength(64);
    }
}
