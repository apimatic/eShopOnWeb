using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        var refundsNavigation = builder.Metadata.FindNavigation(nameof(Payment.Refunds));
        refundsNavigation?.SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(p => p.OrderId).IsUnique();

        builder.Property(p => p.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(p => p.CurrencyCode).IsRequired().HasMaxLength(3);
        builder.Property(p => p.PayPalOrderId).IsRequired().HasMaxLength(64);
        builder.Property(p => p.AuthorizationId).HasMaxLength(64);
        builder.Property(p => p.CaptureId).HasMaxLength(64);

        builder.Property(p => p.Amount).HasPrecision(18, 2);
        builder.Property(p => p.CapturedAmount).HasPrecision(18, 2);
        builder.Property(p => p.PayPalFee).HasPrecision(18, 2);
        builder.Property(p => p.NetAmount).HasPrecision(18, 2);

        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(24).IsRequired();

        builder.HasMany(p => p.Refunds)
            .WithOne()
            .HasForeignKey("PaymentId")
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class PaymentRefundConfiguration : IEntityTypeConfiguration<PaymentRefund>
{
    public void Configure(EntityTypeBuilder<PaymentRefund> builder)
    {
        builder.Property(r => r.PayPalRefundId).IsRequired().HasMaxLength(64);
        builder.Property(r => r.IdempotencyKey).IsRequired().HasMaxLength(128);
        builder.Property(r => r.Status).HasMaxLength(24);
        builder.Property(r => r.Amount).HasPrecision(18, 2);
    }
}
