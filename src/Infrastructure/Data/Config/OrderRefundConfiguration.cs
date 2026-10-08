using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderRefundConfiguration : IEntityTypeConfiguration<OrderRefund>
{
    public void Configure(EntityTypeBuilder<OrderRefund> builder)
    {
        // The key is the duplicate-refund claim ("{orderId}-R{sequence}"): never generated, always set by the domain.
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasMaxLength(32).ValueGeneratedNever();
        builder.HasIndex(r => new { r.OrderId, r.Sequence }).IsUnique();
        builder.HasIndex(r => new { r.OrderId, r.ClientRequestId }).IsUnique();

        builder.Property(r => r.PaymentId).HasMaxLength(32).IsRequired();
        builder.Property(r => r.PaymentPspReference).HasMaxLength(64).IsRequired();
        builder.Property(r => r.IdempotencyKey).HasMaxLength(64).IsRequired();
        builder.HasIndex(r => r.IdempotencyKey).IsUnique();
        builder.Property(r => r.MerchantReference).HasMaxLength(80).IsRequired();
        builder.Property(r => r.Currency).HasMaxLength(3).IsRequired();
        builder.Property(r => r.Amount).IsRequired().HasColumnType("decimal(18,2)");
        builder.Property(r => r.Reason).HasMaxLength(32);
        builder.Property(r => r.RequestedBy).HasMaxLength(256).IsRequired();
        builder.Property(r => r.ClientRequestId).HasMaxLength(64);
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(r => r.PspReference).HasMaxLength(64);
        builder.Property(r => r.Message).HasMaxLength(512);

        builder.Ignore(r => r.CountsAgainstPayment);
        builder.Ignore(r => r.IsUnsettled);
    }
}
