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

        var refunds = builder.Metadata.FindNavigation(nameof(Order.Refunds));
        refunds?.SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(b => b.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(b => b.Status)
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.OwnsOne(o => o.Payment, payment =>
        {
            payment.Property(p => p.Currency).HasMaxLength(3);
            payment.Property(p => p.PayPalOrderId).HasMaxLength(64);
            payment.Property(p => p.PayPalOrderStatus).HasMaxLength(64);
            payment.Property(p => p.AuthorizationId).HasMaxLength(64);
            payment.Property(p => p.AuthorizationStatus).HasMaxLength(64);
            payment.Property(p => p.CaptureId).HasMaxLength(64);
            payment.Property(p => p.CaptureStatus).HasMaxLength(64);
            payment.Property(p => p.AuthorizeRequestId).HasMaxLength(128);
            payment.Property(p => p.CaptureRequestId).HasMaxLength(128);
            payment.Property(p => p.VoidRequestId).HasMaxLength(128);
            payment.Property(p => p.CapturedAmount).HasColumnType("decimal(18,2)");
            payment.Property(p => p.PaypalFee).HasColumnType("decimal(18,2)");
            payment.Property(p => p.NetAmount).HasColumnType("decimal(18,2)");
            payment.Property(p => p.RefundedAmount).HasColumnType("decimal(18,2)");
        });
        builder.Navigation(o => o.Payment).IsRequired();

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
