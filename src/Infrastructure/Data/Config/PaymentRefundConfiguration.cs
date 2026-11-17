using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentRefundConfiguration : IEntityTypeConfiguration<PaymentRefund>
{
    public void Configure(EntityTypeBuilder<PaymentRefund> builder)
    {
        builder.Property(refund => refund.IdempotencyKey)
            .IsRequired()
            .HasMaxLength(200);

        builder.HasIndex(refund => new { refund.PaymentId, refund.IdempotencyKey })
            .IsUnique();

        builder.HasIndex(refund => refund.FinixTransferId);
    }
}
