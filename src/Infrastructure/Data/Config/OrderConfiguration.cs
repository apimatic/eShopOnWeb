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

        // The payment is an optional owned part of the order aggregate. Its owned refund collection
        // is nested beneath it. Owned parts are loaded and saved with the order automatically.
        builder.OwnsOne(o => o.Payment, p =>
        {
            p.WithOwner();

            p.Property(x => x.PayPalOrderId).HasMaxLength(64).IsRequired();
            p.Property(x => x.InvoiceId).HasMaxLength(127).IsRequired();
            p.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            p.Property(x => x.AuthorizationId).HasMaxLength(64).IsRequired();
            p.Property(x => x.AuthorizationStatus).HasMaxLength(32);
            p.Property(x => x.CaptureId).HasMaxLength(64);
            p.Property(x => x.CaptureStatus).HasMaxLength(32);
            p.Property(x => x.CardBrand).HasMaxLength(32);
            p.Property(x => x.CardLast4).HasMaxLength(4);
            p.Property(x => x.Amount).HasColumnType("decimal(18,2)");
            p.Property(x => x.CapturedAmount).HasColumnType("decimal(18,2)");
            p.Property(x => x.PayPalFee).HasColumnType("decimal(18,2)");
            p.Property(x => x.NetAmount).HasColumnType("decimal(18,2)");

            p.OwnsMany(x => x.Refunds, r =>
            {
                r.WithOwner();
                r.Property(x => x.RefundId).HasMaxLength(64).IsRequired();
                r.Property(x => x.Status).HasMaxLength(32);
                r.Property(x => x.IdempotencyKey).HasMaxLength(128).IsRequired();
                r.Property(x => x.Amount).HasColumnType("decimal(18,2)");
            });
            p.Navigation(x => x.Refunds).UsePropertyAccessMode(PropertyAccessMode.Field);
        });
    }
}
