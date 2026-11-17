using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class RefundConfiguration : IEntityTypeConfiguration<Refund>
{
    public void Configure(EntityTypeBuilder<Refund> builder)
    {
        builder.Property(refund => refund.TransferId)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(refund => refund.State)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(refund => refund.IdempotencyKey)
            .IsRequired()
            .HasMaxLength(256);

        builder.HasIndex(refund => new { refund.OrderId, refund.IdempotencyKey })
            .IsUnique();

        builder.HasIndex(refund => refund.TransferId);
    }
}
