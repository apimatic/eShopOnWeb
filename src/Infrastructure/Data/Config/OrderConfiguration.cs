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

        builder.OwnsOne(o => o.Payment, p =>
        {
            p.WithOwner();
            p.Property(x => x.PayPalOrderId).HasMaxLength(100);
            p.Property(x => x.AuthorizationId).HasMaxLength(100);
            p.Property(x => x.CaptureId).HasMaxLength(100);
            p.Property(x => x.CapturedAmountValue).HasMaxLength(20);
            p.Property(x => x.CapturedAmountCurrency).HasMaxLength(3);
            p.Property(x => x.PayPalFeeValue).HasMaxLength(20);
            p.Property(x => x.NetAmountValue).HasMaxLength(20);
            p.Property(x => x.TotalRefunded).HasPrecision(18, 2);
        });
    }
}
