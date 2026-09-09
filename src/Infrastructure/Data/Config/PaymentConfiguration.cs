using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
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

        builder.HasIndex(p => p.OrderId).IsUnique();
        builder.HasIndex(p => p.BuyerId);

        builder.Property(p => p.AuthorizedAmount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.CapturedGrossAmount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.PayPalFee).HasColumnType("decimal(18,2)");
        builder.Property(p => p.NetAmount).HasColumnType("decimal(18,2)");

        builder.Property(p => p.Status)
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.Property(p => p.PayPalOrderId).HasMaxLength(255);
        builder.Property(p => p.AuthorizationId).HasMaxLength(255);
        builder.Property(p => p.CaptureId).HasMaxLength(255);

        // Refunds are part of the Payment aggregate; map them as an owned collection.
        builder.OwnsMany(p => p.Refunds, refund =>
        {
            refund.WithOwner();
            refund.Property(r => r.Amount).HasColumnType("decimal(18,2)");
            refund.Property(r => r.PayPalRefundId).IsRequired().HasMaxLength(255);
            refund.Property(r => r.Status).IsRequired().HasMaxLength(30);
            refund.Property(r => r.IdempotencyKey).IsRequired().HasMaxLength(255);
        });

        builder.Metadata.FindNavigation(nameof(Payment.Refunds))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
