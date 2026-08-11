using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        var navigation = builder.Metadata.FindNavigation(nameof(Order.OrderItems));

        navigation?.SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(b => b.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.OwnsOne(o => o.ShipToAddress, a =>
        {
            a.WithOwner();

            a.Property(a => a.ZipCode)
                .HasMaxLength(18)
                .IsRequired();

            a.Property(a => a.Street)
                .HasMaxLength(180)
                .IsRequired();

            a.Property(a => a.State)
                .HasMaxLength(60);

            a.Property(a => a.Country)
                .HasMaxLength(90)
                .IsRequired();

            a.Property(a => a.City)
                .HasMaxLength(100)
                .IsRequired();
        });

        builder.Navigation(x => x.ShipToAddress).IsRequired();

        // Additive payment / fulfilment state.
        builder.Property(o => o.Status)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        // The PayPal-owned payment state is an optional owned member of the order.
        builder.OwnsOne(o => o.Payment, payment =>
        {
            payment.WithOwner();

            payment.Property(p => p.Currency).HasMaxLength(3).IsRequired();
            payment.Property(p => p.Nonce).HasMaxLength(40).IsRequired();
            payment.Property(p => p.Amount).HasColumnType("decimal(18,2)");
            payment.Property(p => p.CapturedGross).HasColumnType("decimal(18,2)");
            payment.Property(p => p.PayPalFee).HasColumnType("decimal(18,2)");
            payment.Property(p => p.NetAmount).HasColumnType("decimal(18,2)");
            payment.Property(p => p.InstrumentDescription).HasMaxLength(100);
            payment.Property(p => p.InvoiceReference).HasMaxLength(64);
            payment.Property(p => p.PayPalOrderId).HasMaxLength(64);
            payment.Property(p => p.AuthorizationId).HasMaxLength(64);
            payment.Property(p => p.AuthorizationStatus).HasMaxLength(30);
            payment.Property(p => p.CaptureId).HasMaxLength(64);
            payment.Property(p => p.CaptureStatus).HasMaxLength(30);

            // Computed, non-persisted projections.
            payment.Ignore(p => p.RefundedAmount);
            payment.Ignore(p => p.RefundableRemaining);
            payment.Ignore(p => p.HasAuthorization);
            payment.Ignore(p => p.HasCapture);

            // Refunds are an owned collection of the payment, populated via the backing field.
            payment.OwnsMany(p => p.Refunds, refund =>
            {
                refund.WithOwner();
                refund.Property(r => r.RefundId).HasMaxLength(64);
                refund.Property(r => r.Amount).HasColumnType("decimal(18,2)");
                refund.Property(r => r.Status).HasMaxLength(30);
                refund.Property(r => r.IdempotencyKey).HasMaxLength(128);
            });
            payment.Navigation(p => p.Refunds).UsePropertyAccessMode(PropertyAccessMode.Field);
        });
    }
}
