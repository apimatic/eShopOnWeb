using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderPaymentConfiguration : IEntityTypeConfiguration<OrderPayment>
{
    public void Configure(EntityTypeBuilder<OrderPayment> builder)
    {
        var navigation = builder.Metadata.FindNavigation(nameof(OrderPayment.Refunds));
        navigation?.SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(p => p.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(p => p.Currency).IsRequired().HasMaxLength(3);
        builder.Property(p => p.Amount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.PaymentMethodDescription).HasMaxLength(256);
        builder.Property(p => p.PayPalOrderId).HasMaxLength(64);
        builder.Property(p => p.AuthorizationId).HasMaxLength(64);
        builder.Property(p => p.AuthorizationStatus).HasMaxLength(32);
        builder.Property(p => p.CaptureId).HasMaxLength(64);
        builder.Property(p => p.CaptureStatus).HasMaxLength(32);
        builder.Property(p => p.CapturedAmount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.CaptureFee).HasColumnType("decimal(18,2)");
        builder.Property(p => p.CaptureNetAmount).HasColumnType("decimal(18,2)");

        builder.HasIndex(p => p.OrderId).IsUnique();
    }
}

public class PaymentRefundConfiguration : IEntityTypeConfiguration<PaymentRefund>
{
    public void Configure(EntityTypeBuilder<PaymentRefund> builder)
    {
        builder.Property(r => r.PayPalRefundId).IsRequired().HasMaxLength(64);
        builder.Property(r => r.Amount).HasColumnType("decimal(18,2)");
        builder.Property(r => r.Currency).IsRequired().HasMaxLength(3);
        builder.Property(r => r.Status).IsRequired().HasMaxLength(32);
        builder.Property(r => r.IdempotencyKey).IsRequired().HasMaxLength(128);

        builder.HasIndex(r => r.IdempotencyKey).IsUnique();
    }
}

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.Property(m => m.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(m => m.VaultTokenId).IsRequired().HasMaxLength(64);
        builder.Property(m => m.PayPalCustomerId).HasMaxLength(64);
        builder.Property(m => m.Brand).IsRequired().HasMaxLength(32);
        builder.Property(m => m.LastDigits).IsRequired().HasMaxLength(4);
        builder.Property(m => m.ExpiryMonth).IsRequired().HasMaxLength(2);
        builder.Property(m => m.ExpiryYear).IsRequired().HasMaxLength(4);

        builder.HasIndex(m => m.VaultTokenId).IsUnique();
    }
}
