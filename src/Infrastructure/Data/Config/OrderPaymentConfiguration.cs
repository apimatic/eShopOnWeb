using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderPaymentConfiguration : IEntityTypeConfiguration<OrderPayment>
{
    public void Configure(EntityTypeBuilder<OrderPayment> builder)
    {
        // One payment per order; the FK lives on the payment.
        builder.HasOne<Order>()
            .WithOne(o => o.Payment)
            .HasForeignKey<OrderPayment>("OrderId")
            .IsRequired();

        // Refunds are owned through the payment; map the read-only collection to its backing field.
        builder.HasMany(p => p.Refunds)
            .WithOne()
            .HasForeignKey("OrderPaymentId")
            .IsRequired();
        builder.Metadata.FindNavigation(nameof(OrderPayment.Refunds))
            ?.SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(p => p.PayPalOrderId).IsRequired().HasMaxLength(64);
        builder.Property(p => p.PayPalCustomId).HasMaxLength(64);
        builder.Property(p => p.CurrencyCode).IsRequired().HasMaxLength(3);
        builder.Property(p => p.AuthorizationId).HasMaxLength(64);
        builder.Property(p => p.CaptureId).HasMaxLength(64);
        builder.Property(p => p.AuthorizationStatus).HasMaxLength(32);
        builder.Property(p => p.CaptureStatus).HasMaxLength(32);
        builder.Property(p => p.InstrumentSummary).HasMaxLength(64);
        builder.Property(p => p.VaultId).HasMaxLength(128);

        builder.Property(p => p.Amount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.CapturedAmount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.PayPalFee).HasColumnType("decimal(18,2)");
        builder.Property(p => p.NetAmount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.RefundedAmount).HasColumnType("decimal(18,2)");
    }
}
