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

        builder.Property(p => p.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(p => p.Currency).IsRequired().HasMaxLength(3);
        builder.Property(p => p.PayPalOrderId).HasMaxLength(64);
        builder.Property(p => p.AuthorizationId).HasMaxLength(64);
        builder.Property(p => p.AuthorizationStatus).HasMaxLength(32);
        builder.Property(p => p.CaptureId).HasMaxLength(64);
        builder.Property(p => p.CaptureStatus).HasMaxLength(32);
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(32);

        builder.Property(p => p.AuthorizedAmount).HasPrecision(18, 2);
        builder.Property(p => p.CapturedAmount).HasPrecision(18, 2);
        builder.Property(p => p.PayPalFee).HasPrecision(18, 2);
        builder.Property(p => p.NetAmount).HasPrecision(18, 2);
        builder.Property(p => p.RefundedAmount).HasPrecision(18, 2);

        builder.HasMany(p => p.Refunds)
            .WithOne()
            .HasForeignKey(r => r.PaymentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class PaymentRefundConfiguration : IEntityTypeConfiguration<PaymentRefund>
{
    public void Configure(EntityTypeBuilder<PaymentRefund> builder)
    {
        builder.Property(r => r.PayPalRefundId).IsRequired().HasMaxLength(64);
        builder.Property(r => r.Status).IsRequired().HasMaxLength(32);
        builder.Property(r => r.IdempotencyKey).IsRequired().HasMaxLength(128);
        builder.Property(r => r.Amount).HasPrecision(18, 2);
        builder.HasIndex(r => r.IdempotencyKey);
    }
}

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.Property(m => m.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(m => m.PayPalCustomerId).HasMaxLength(64);
        builder.Property(m => m.VaultTokenId).IsRequired().HasMaxLength(64);
        builder.Property(m => m.CardBrand).HasMaxLength(32);
        builder.Property(m => m.LastDigits).HasMaxLength(4);
        builder.Property(m => m.Expiry).HasMaxLength(7);
        builder.Property(m => m.CardholderName).HasMaxLength(256);
        builder.HasIndex(m => m.BuyerId);
    }
}
