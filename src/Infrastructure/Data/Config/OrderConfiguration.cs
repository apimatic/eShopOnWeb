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

        var refundNavigation = builder.Metadata.FindNavigation(nameof(Order.Refunds));
        refundNavigation?.SetPropertyAccessMode(PropertyAccessMode.Field);

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

        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.PaymentReference).HasMaxLength(64).IsRequired();
        builder.Property(x => x.PaymentStatus).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.FulfilmentStatus).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.PayPalOrderId).HasMaxLength(128);
        builder.Property(x => x.PayPalOrderStatus).HasMaxLength(32);
        builder.Property(x => x.PayPalAuthorizationId).HasMaxLength(128);
        builder.Property(x => x.PayPalAuthorizationStatus).HasMaxLength(32);
        builder.Property(x => x.PayPalCaptureId).HasMaxLength(128);
        builder.Property(x => x.PayPalCaptureStatus).HasMaxLength(32);
        builder.Property(x => x.CapturedAmount).HasColumnType("decimal(18,2)");
        builder.Property(x => x.PayPalFee).HasColumnType("decimal(18,2)");
        builder.Property(x => x.NetProceeds).HasColumnType("decimal(18,2)");
        builder.Property(x => x.CreatePaymentRequestId).HasMaxLength(108).IsRequired();
        builder.Property(x => x.AuthorizePaymentRequestId).HasMaxLength(108).IsRequired();
        builder.Property(x => x.CapturePaymentRequestId).HasMaxLength(108).IsRequired();
        builder.Property(x => x.VoidPaymentRequestId).HasMaxLength(108).IsRequired();
        builder.Property(x => x.ReauthorizePaymentRequestId).HasMaxLength(108);
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => x.PayPalOrderId).IsUnique().HasFilter("[PayPalOrderId] IS NOT NULL");
        builder.HasIndex(x => x.PaymentReference).IsUnique();
        builder.HasIndex(x => x.PayPalAuthorizationId).IsUnique().HasFilter("[PayPalAuthorizationId] IS NOT NULL");
        builder.HasIndex(x => x.PayPalCaptureId).IsUnique().HasFilter("[PayPalCaptureId] IS NOT NULL");
    }
}
