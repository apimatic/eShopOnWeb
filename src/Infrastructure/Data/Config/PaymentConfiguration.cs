using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedCardAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderPaymentConfiguration : IEntityTypeConfiguration<OrderPayment>
{
    public void Configure(EntityTypeBuilder<OrderPayment> builder)
    {
        builder.HasIndex(p => p.OrderId).IsUnique();

        builder.Property(p => p.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(p => p.CurrencyCode).IsRequired().HasMaxLength(3);
        builder.Property(p => p.InvoiceId).IsRequired().HasMaxLength(127);
        builder.Property(p => p.Amount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.CapturedAmount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.PayPalFee).HasColumnType("decimal(18,2)");
        builder.Property(p => p.NetAmount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.State).HasConversion<int>();

        var refunds = builder.Metadata.FindNavigation(nameof(OrderPayment.Refunds));
        refunds?.SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(p => p.Refunds)
            .WithOne()
            .HasForeignKey(r => r.OrderPaymentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class PaymentRefundConfiguration : IEntityTypeConfiguration<PaymentRefund>
{
    public void Configure(EntityTypeBuilder<PaymentRefund> builder)
    {
        builder.Property(r => r.IdempotencyKey).IsRequired().HasMaxLength(128);
        builder.Property(r => r.Amount).HasColumnType("decimal(18,2)");
        builder.Property(r => r.PayPalRefundId).HasMaxLength(64);
        builder.Property(r => r.Status).HasMaxLength(32);

        // Caller idempotency key is unique per order payment: repeating a refund under the same key cannot refund twice.
        builder.HasIndex(r => new { r.OrderPaymentId, r.IdempotencyKey }).IsUnique();
    }
}

public class PaymentOperationConfiguration : IEntityTypeConfiguration<PaymentOperation>
{
    public void Configure(EntityTypeBuilder<PaymentOperation> builder)
    {
        builder.Property(o => o.OperationType).IsRequired().HasMaxLength(32);

        // One authorize/capture/void claim per order payment; the second concurrent insert is rejected.
        builder.HasIndex(o => new { o.OrderPaymentId, o.OperationType }).IsUnique();
    }
}

public class SavedCardConfiguration : IEntityTypeConfiguration<SavedCard>
{
    public void Configure(EntityTypeBuilder<SavedCard> builder)
    {
        builder.Property(c => c.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(c => c.PayPalVaultId).IsRequired().HasMaxLength(255);
        builder.Property(c => c.Brand).HasMaxLength(64);
        builder.Property(c => c.LastDigits).HasMaxLength(4);
        builder.Property(c => c.Expiry).HasMaxLength(7);
        builder.Property(c => c.CardholderName).HasMaxLength(300);

        builder.HasIndex(c => new { c.BuyerId, c.PayPalVaultId }).IsUnique();
    }
}
