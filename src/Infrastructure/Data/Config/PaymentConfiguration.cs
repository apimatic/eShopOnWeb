using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        var navigation = builder.Metadata.FindNavigation(nameof(Payment.Refunds));
        navigation?.SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(p => p.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(p => p.Currency)
            .IsRequired()
            .HasMaxLength(3);

        builder.Property(p => p.Amount).HasPrecision(18, 2);
        builder.Property(p => p.CapturedAmount).HasPrecision(18, 2);
        builder.Property(p => p.SellerFee).HasPrecision(18, 2);
        builder.Property(p => p.NetAmount).HasPrecision(18, 2);

        builder.Property(p => p.Status)
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(p => p.PayPalOrderId).HasMaxLength(64);
        builder.Property(p => p.InvoiceId).HasMaxLength(127);
        builder.Property(p => p.AuthorizationId).HasMaxLength(64);
        builder.Property(p => p.AuthorizationStatus).HasMaxLength(32);
        builder.Property(p => p.CaptureId).HasMaxLength(64);
        builder.Property(p => p.CaptureStatus).HasMaxLength(32);

        builder.HasIndex(p => p.OrderId);

        builder.OwnsMany(p => p.Refunds, refund =>
        {
            refund.WithOwner().HasForeignKey(r => r.PaymentId);
            refund.Property(r => r.IdempotencyKey).IsRequired().HasMaxLength(128);
            refund.Property(r => r.PayPalRefundId).HasMaxLength(64);
            refund.Property(r => r.Currency).IsRequired().HasMaxLength(3);
            refund.Property(r => r.Status).HasMaxLength(32);
            refund.Property(r => r.Amount).HasPrecision(18, 2);
            refund.HasIndex(r => new { r.PaymentId, r.IdempotencyKey }).IsUnique();
        });
    }
}
