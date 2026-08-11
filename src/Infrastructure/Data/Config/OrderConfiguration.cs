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

        builder.Property(o => o.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        // The payment (PayPal-owned state) is part of the Order aggregate.
        builder.OwnsOne(o => o.Payment, payment =>
        {
            payment.WithOwner();

            payment.Property(p => p.PayPalOrderId).HasMaxLength(64);
            payment.Property(p => p.Currency).HasMaxLength(3);
            payment.Property(p => p.CardBrand).HasMaxLength(32);
            payment.Property(p => p.CardLast4).HasMaxLength(4);
            payment.Property(p => p.AuthorizationId).HasMaxLength(64);
            payment.Property(p => p.AuthorizationStatus).HasMaxLength(32);
            payment.Property(p => p.CaptureId).HasMaxLength(64);
            payment.Property(p => p.CaptureStatus).HasMaxLength(32);

            payment.OwnsMany(p => p.Refunds, refund =>
            {
                refund.WithOwner();
                refund.Property(r => r.IdempotencyKey).HasMaxLength(128).IsRequired();
                refund.Property(r => r.PayPalRefundId).HasMaxLength(64).IsRequired();
                refund.Property(r => r.Status).HasMaxLength(32);
            });
            payment.Navigation(p => p.Refunds).UsePropertyAccessMode(PropertyAccessMode.Field);
        });
        builder.Navigation(o => o.Payment).IsRequired(false);

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
    }
}
