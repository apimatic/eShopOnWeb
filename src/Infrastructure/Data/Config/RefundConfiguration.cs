using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class RefundConfiguration : IEntityTypeConfiguration<Refund>
{
    public void Configure(EntityTypeBuilder<Refund> builder)
    {
        builder.ToTable("Refunds");

        builder.Property(r => r.RefundId)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(r => r.IdempotencyKey)
            .HasMaxLength(255);

        builder.Property(r => r.Amount)
            .HasColumnType("decimal(18,2)");

        builder.Property(r => r.Status)
            .HasMaxLength(32)
            .IsRequired();

        builder.HasIndex(r => new { r.PaymentId, r.IdempotencyKey });
    }
}
