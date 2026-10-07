using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderRefundConfiguration : IEntityTypeConfiguration<OrderRefund>
{
    public void Configure(EntityTypeBuilder<OrderRefund> builder)
    {
        // The primary key is the refund claim: the same operator idempotency key maps to the same id.
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.IdempotencyKey).HasMaxLength(64).IsRequired();
        builder.Property(r => r.MerchantReference).HasMaxLength(80).IsRequired();
        builder.Property(r => r.Currency).HasMaxLength(3).IsRequired();
        builder.Property(r => r.Reason).HasMaxLength(256);
        builder.Property(r => r.RequestedBy).HasMaxLength(256).IsRequired();
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(r => r.PspReference).HasMaxLength(64);
        builder.Property(r => r.FailureMessage).HasMaxLength(1024);
    }
}
