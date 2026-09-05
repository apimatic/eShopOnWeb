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

        builder.ToTable("Payments");

        builder.Property(p => p.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(p => p.Currency)
            .HasMaxLength(3)
            .IsRequired();

        builder.Property(p => p.Amount)
            .HasColumnType("decimal(18,2)")
            .IsRequired();

        builder.Property(p => p.CapturedAmount)
            .HasColumnType("decimal(18,2)");

        builder.Property(p => p.PayPalFee)
            .HasColumnType("decimal(18,2)");

        builder.Property(p => p.NetAmount)
            .HasColumnType("decimal(18,2)");

        builder.HasIndex(p => p.OrderId)
            .IsUnique();

        builder.Property(p => p.PayPalOrderId).HasMaxLength(64);
        builder.Property(p => p.PayPalAuthorizationId).HasMaxLength(64);
        builder.Property(p => p.PayPalAuthorizationStatus).HasMaxLength(32);
        builder.Property(p => p.PayPalCaptureId).HasMaxLength(64);
        builder.Property(p => p.PayPalCaptureStatus).HasMaxLength(32);

        builder.HasMany(p => p.Refunds)
            .WithOne()
            .HasForeignKey(r => r.PaymentId);
    }
}

public class PaymentRefundConfiguration : IEntityTypeConfiguration<PaymentRefund>
{
    public void Configure(EntityTypeBuilder<PaymentRefund> builder)
    {
        builder.ToTable("PaymentRefunds");

        builder.Property(r => r.IdempotencyKey)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(r => r.PayPalRefundId).HasMaxLength(64);
        builder.Property(r => r.Status).HasMaxLength(32);
        builder.Property(r => r.Currency).HasMaxLength(3);
        builder.Property(r => r.Amount)
            .HasColumnType("decimal(18,2)")
            .IsRequired();

        // The idempotency contract: the same key can only ever produce one refund per payment.
        builder.HasIndex(r => new { r.PaymentId, r.IdempotencyKey })
            .IsUnique();
    }
}

public class SavedCardConfiguration : IEntityTypeConfiguration<SavedCard>
{
    public void Configure(EntityTypeBuilder<SavedCard> builder)
    {
        builder.ToTable("SavedCards");

        builder.Property(c => c.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(c => c.VaultTokenId)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(c => c.PayPalCustomerId).HasMaxLength(64);
        builder.Property(c => c.Brand).HasMaxLength(32);
        builder.Property(c => c.LastFourDigits).HasMaxLength(4);
        builder.Property(c => c.Expiry).HasMaxLength(16);
        builder.Property(c => c.CardholderName).HasMaxLength(120);

        builder.HasIndex(c => c.BuyerId);
        builder.HasIndex(c => c.VaultTokenId)
            .IsUnique();
    }
}
