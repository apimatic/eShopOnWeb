using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentRecordConfiguration : IEntityTypeConfiguration<PaymentRecord>
{
    public void Configure(EntityTypeBuilder<PaymentRecord> builder)
    {
        var navigation = builder.Metadata.FindNavigation(nameof(PaymentRecord.Refunds));
        navigation?.SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(p => p.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(p => p.Status).IsRequired().HasMaxLength(50);
        builder.Property(p => p.PaymentIdempotencyKey).HasMaxLength(256);
        builder.Property(p => p.PayPalOrderId).HasMaxLength(256);
        builder.Property(p => p.AuthorizationId).HasMaxLength(256);
        builder.Property(p => p.AuthorizationStatus).HasMaxLength(50);
        builder.Property(p => p.CaptureId).HasMaxLength(256);
        builder.Property(p => p.CaptureStatus).HasMaxLength(50);
        builder.Property(p => p.CapturedAmount).HasMaxLength(50);
        builder.Property(p => p.CapturedCurrency).HasMaxLength(10);
        builder.Property(p => p.PayPalFee).HasMaxLength(50);
        builder.Property(p => p.NetAmount).HasMaxLength(50);

        builder.HasMany(p => p.Refunds)
               .WithOne()
               .HasForeignKey("PaymentRecordId")
               .OnDelete(DeleteBehavior.Cascade);
    }
}

public class PaymentRefundConfiguration : IEntityTypeConfiguration<PaymentRefund>
{
    public void Configure(EntityTypeBuilder<PaymentRefund> builder)
    {
        builder.Property(r => r.RefundId).HasMaxLength(256);
        builder.Property(r => r.RefundStatus).HasMaxLength(50);
        builder.Property(r => r.Amount).HasMaxLength(50);
        builder.Property(r => r.Currency).HasMaxLength(10);
        builder.Property(r => r.IdempotencyKey).IsRequired().HasMaxLength(256);
    }
}
