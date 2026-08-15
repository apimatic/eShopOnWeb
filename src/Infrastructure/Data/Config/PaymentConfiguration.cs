using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.Property(p => p.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(p => p.Currency).IsRequired().HasMaxLength(3);
        builder.Property(p => p.Amount).HasColumnType("decimal(18,2)").IsRequired();

        builder.Property(p => p.PayPalOrderId).IsRequired().HasMaxLength(64);
        builder.Property(p => p.AuthorizationId).IsRequired().HasMaxLength(64);
        builder.Property(p => p.AuthorizationStatus).HasMaxLength(40);
        builder.Property(p => p.CaptureId).HasMaxLength(64);
        builder.Property(p => p.CaptureStatus).HasMaxLength(40);

        builder.Property(p => p.CapturedAmount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.PayPalFee).HasColumnType("decimal(18,2)");
        builder.Property(p => p.NetAmount).HasColumnType("decimal(18,2)");

        builder.Property(p => p.Status)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        // One payment per order.
        builder.HasIndex(p => p.OrderId).IsUnique();

        // Refunds are part of the Payment aggregate, held behind the private _refunds field.
        builder.OwnsMany(p => p.Refunds, r =>
        {
            r.WithOwner().HasForeignKey("PaymentId");
            r.Property(x => x.RefundId).IsRequired().HasMaxLength(64);
            r.Property(x => x.Amount).HasColumnType("decimal(18,2)").IsRequired();
            r.Property(x => x.Status).HasMaxLength(40);
            r.Property(x => x.IdempotencyKey).IsRequired().HasMaxLength(128);
        });
        builder.Metadata.FindNavigation(nameof(Payment.Refunds))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
