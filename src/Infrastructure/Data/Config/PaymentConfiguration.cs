using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.Property(p => p.BuyerId)
            .IsRequired()
            .HasMaxLength(256);
        builder.Property(p => p.Currency)
            .IsRequired()
            .HasMaxLength(3);
        builder.Property(p => p.AuthorizationStatus)
            .HasMaxLength(50);
        builder.HasIndex(p => p.OrderId).IsUnique();

        builder.HasOne<Order>()
            .WithOne(o => o.Payment)
            .HasForeignKey<Payment>(p => p.OrderId);

        var refundsNavigation = builder.Metadata.FindNavigation(nameof(Payment.Refunds));
        refundsNavigation?.SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(p => p.Refunds)
            .WithOne()
            .HasForeignKey(r => r.PaymentId);
    }
}

public class PaymentRefundConfiguration : IEntityTypeConfiguration<PaymentRefund>
{
    public void Configure(EntityTypeBuilder<PaymentRefund> builder)
    {
        builder.Property(r => r.IdempotencyKey)
            .IsRequired()
            .HasMaxLength(200);
        builder.Property(r => r.Status)
            .HasMaxLength(50);

        builder.HasIndex(r => new { r.PaymentId, r.IdempotencyKey }).IsUnique();
    }
}

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.Property(m => m.BuyerId)
            .IsRequired()
            .HasMaxLength(256);
        builder.Property(m => m.PayPalPaymentTokenId)
            .IsRequired()
            .HasMaxLength(100);
        builder.Property(m => m.Brand)
            .HasMaxLength(50);
        builder.Property(m => m.Last4)
            .HasMaxLength(4)
            .IsRequired();
        builder.Property(m => m.Expiry)
            .HasMaxLength(7);
        builder.HasIndex(m => m.BuyerId);
    }
}
