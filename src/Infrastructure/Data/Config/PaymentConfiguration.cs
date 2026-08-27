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
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(p => p.PayPalOrderId).HasMaxLength(64);
        builder.Property(p => p.AuthorizationId).HasMaxLength(64);
        builder.Property(p => p.AuthorizationStatus).HasMaxLength(32);
        builder.Property(p => p.AuthorizeRequestId).HasMaxLength(128);
        builder.Property(p => p.CaptureRequestId).HasMaxLength(128);
        builder.Property(p => p.CaptureId).HasMaxLength(64);
        builder.Property(p => p.CaptureStatus).HasMaxLength(32);

        builder.Property(p => p.AuthorizedAmount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.CapturedAmount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.PayPalFee).HasColumnType("decimal(18,2)");
        builder.Property(p => p.NetAmount).HasColumnType("decimal(18,2)");

        builder.HasIndex(p => p.OrderId);
    }
}

public class PaymentRefundConfiguration : IEntityTypeConfiguration<PaymentRefund>
{
    public void Configure(EntityTypeBuilder<PaymentRefund> builder)
    {
        builder.Property(r => r.PayPalRefundId).IsRequired().HasMaxLength(64);
        builder.Property(r => r.Status).IsRequired().HasMaxLength(32);
        builder.Property(r => r.IdempotencyKey).IsRequired().HasMaxLength(128);
        builder.Property(r => r.Amount).HasColumnType("decimal(18,2)");

        builder.HasIndex(r => r.IdempotencyKey);
    }
}

public class SavedCardConfiguration : IEntityTypeConfiguration<SavedCard>
{
    public void Configure(EntityTypeBuilder<SavedCard> builder)
    {
        builder.Property(c => c.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(c => c.VaultTokenId).IsRequired().HasMaxLength(64);
        builder.Property(c => c.Brand).HasMaxLength(32);
        builder.Property(c => c.LastDigits).HasMaxLength(4);
        builder.Property(c => c.Expiry).HasMaxLength(7);
        builder.Property(c => c.CardholderName).HasMaxLength(256);

        builder.HasIndex(c => c.BuyerId);
    }
}
