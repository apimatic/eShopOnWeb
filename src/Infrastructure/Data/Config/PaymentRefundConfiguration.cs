using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public sealed class PaymentRefundConfiguration : IEntityTypeConfiguration<PaymentRefund>
{
    public void Configure(EntityTypeBuilder<PaymentRefund> builder)
    {
        builder.HasIndex(x => new { x.PaymentRecordId, x.IdempotencyKey }).IsUnique();
        builder.Property(x => x.IdempotencyKey).HasMaxLength(108).IsRequired();
        builder.Property(x => x.RequestedAmount).HasColumnType("decimal(18,2)");
        builder.Property(x => x.RefundedAmount).HasColumnType("decimal(18,2)");
    }
}
