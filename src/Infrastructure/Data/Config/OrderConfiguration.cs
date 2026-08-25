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

        builder.Property(b => b.Status)
            .IsRequired();

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
            p.Property(x => x.PayPalOrderId).HasMaxLength(64);
            p.Property(x => x.AuthorizationId).HasMaxLength(64);
            p.Property(x => x.AuthorizationStatus).HasMaxLength(32);
            p.Property(x => x.AuthorizationExpirationTime).HasMaxLength(64);
            p.Property(x => x.CaptureId).HasMaxLength(64);
            p.Property(x => x.CaptureStatus).HasMaxLength(32);
            p.Property(x => x.RefundsJson).HasMaxLength(8000);
        });
    }
}
