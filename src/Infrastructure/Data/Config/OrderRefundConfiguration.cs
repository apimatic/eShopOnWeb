using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderRefundConfiguration : IEntityTypeConfiguration<OrderRefund>
{
    public void Configure(EntityTypeBuilder<OrderRefund> builder)
    {
        // The composite primary key is the refund claim: concurrent refunds on one order both try to insert the
        // same (OrderId, Sequence), so refunds are serialised and the "never beyond what was paid" check holds.
        builder.HasKey(r => new { r.OrderId, r.Sequence });
        builder.Property(r => r.Sequence).ValueGeneratedNever();
        builder.HasIndex(r => r.RefundId).IsUnique();
        builder.HasIndex(r => new { r.OrderId, r.ClientRequestKey }).IsUnique();

        builder.Property(r => r.IdempotencyKey).HasMaxLength(64).IsRequired();
        builder.Property(r => r.ClientRequestKey).HasMaxLength(64);
        builder.Property(r => r.Reference).HasMaxLength(80).IsRequired();
        builder.Property(r => r.Amount).HasColumnType("decimal(18,2)");
        builder.Property(r => r.Currency).HasMaxLength(3).IsRequired();
        builder.Property(r => r.Reason).HasMaxLength(32);
        builder.Property(r => r.PaymentPspReference).HasMaxLength(64).IsRequired();
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(r => r.PspReference).HasMaxLength(64);
        builder.Property(r => r.ErrorCode).HasMaxLength(64);
        builder.Property(r => r.ErrorMessage).HasMaxLength(1024);
        builder.Ignore(r => r.ReservesAmount);
    }
}
