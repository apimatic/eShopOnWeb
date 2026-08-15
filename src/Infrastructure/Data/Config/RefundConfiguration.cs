using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class RefundConfiguration : IEntityTypeConfiguration<Refund>
{
    public void Configure(EntityTypeBuilder<Refund> builder)
    {
        builder.HasKey(r => r.Id);

        builder.Property(r => r.PayPalRefundId).IsRequired().HasMaxLength(64);
        builder.Property(r => r.Status).HasMaxLength(30);
        builder.Property(r => r.IdempotencyKey).IsRequired().HasMaxLength(128);
        builder.Property(r => r.Amount).HasColumnType("decimal(18,2)");
    }
}
