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

        builder.Property(b => b.Currency).HasMaxLength(3);
        builder.Property(b => b.PaymentState).HasConversion<string>().HasMaxLength(32);
        builder.Property(b => b.PayPalOrderId).HasMaxLength(64);
        builder.Property(b => b.PayPalAuthorizationId).HasMaxLength(64);
        builder.Property(b => b.PayPalAuthorizationStatus).HasMaxLength(32);
        builder.Property(b => b.PayPalCaptureId).HasMaxLength(64);
        builder.Property(b => b.PayPalCaptureStatus).HasMaxLength(32);
        builder.Property(b => b.AuthorizedAmount).HasPrecision(18, 2);
        builder.Property(b => b.CapturedAmount).HasPrecision(18, 2);
        builder.Property(b => b.PayPalFee).HasPrecision(18, 2);
        builder.Property(b => b.NetProceeds).HasPrecision(18, 2);
        builder.Property(b => b.RefundedAmount).HasPrecision(18, 2);
        builder.Property(b => b.PayPalCreateRequestId).HasMaxLength(108);
        builder.Property(b => b.PayPalAuthorizeRequestId).HasMaxLength(108);
        builder.Property(b => b.PayPalCaptureRequestId).HasMaxLength(108);
        builder.Property(b => b.PayPalVoidRequestId).HasMaxLength(108);
        builder.Property(b => b.PayPalReauthorizeRequestId).HasMaxLength(108);

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
