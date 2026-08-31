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

        var refundsNavigation = builder.Metadata.FindNavigation(nameof(Order.Refunds));
        refundsNavigation?.SetPropertyAccessMode(PropertyAccessMode.Field);

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

        builder.Property(o => o.Currency).HasMaxLength(3);
        builder.Property(o => o.AuthorizedAmount).HasPrecision(18, 2);
        builder.Property(o => o.CapturedAmount).HasPrecision(18, 2);
        builder.Property(o => o.PayPalFee).HasPrecision(18, 2);
        builder.Property(o => o.NetProceeds).HasPrecision(18, 2);
        builder.Property(o => o.PayPalOrderId).HasMaxLength(64);
        builder.Property(o => o.PayPalOrderStatus).HasMaxLength(32);
        builder.Property(o => o.AuthorizationId).HasMaxLength(64);
        builder.Property(o => o.AuthorizationStatus).HasMaxLength(32);
        builder.Property(o => o.CaptureId).HasMaxLength(64);
        builder.Property(o => o.CaptureStatus).HasMaxLength(32);
        builder.Property(o => o.CreatePaymentRequestId).HasMaxLength(64);
        builder.Property(o => o.AuthorizeRequestId).HasMaxLength(64);
        builder.Property(o => o.CaptureRequestId).HasMaxLength(64);
        builder.Property(o => o.VoidRequestId).HasMaxLength(64);
        builder.Property(o => o.ReauthorizeRequestId).HasMaxLength(64);
        builder.Property(o => o.RowVersion).IsRowVersion();

        builder.HasMany(o => o.Refunds)
            .WithOne()
            .HasForeignKey(r => r.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
