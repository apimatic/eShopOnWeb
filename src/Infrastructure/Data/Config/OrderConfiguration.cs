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
            .HasConversion<int>();

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

        // The payment facet (PayPal-owned state) is an optional owned entity of the order aggregate.
        builder.OwnsOne(o => o.Payment, p =>
        {
            p.WithOwner();

            p.Property(x => x.Amount).HasColumnType("decimal(18,2)");
            p.Property(x => x.CapturedGross).HasColumnType("decimal(18,2)");
            p.Property(x => x.PayPalFee).HasColumnType("decimal(18,2)");
            p.Property(x => x.NetAmount).HasColumnType("decimal(18,2)");
            p.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            p.Property(x => x.MerchantReference).HasMaxLength(127);
            p.Property(x => x.PayPalOrderId).HasMaxLength(64);
            p.Property(x => x.AuthorizationId).HasMaxLength(64);
            p.Property(x => x.CaptureId).HasMaxLength(64);

            // Each refund the payment has issued, owned by the payment.
            p.OwnsMany(x => x.Refunds, r =>
            {
                r.WithOwner();
                r.Property(x => x.Amount).HasColumnType("decimal(18,2)");
                r.Property(x => x.IdempotencyKey).HasMaxLength(128).IsRequired();
                r.Property(x => x.PayPalRefundId).HasMaxLength(64).IsRequired();
                r.Property(x => x.Status).HasMaxLength(32);
            });
            p.Navigation(x => x.Refunds).UsePropertyAccessMode(PropertyAccessMode.Field);
        });
    }
}
