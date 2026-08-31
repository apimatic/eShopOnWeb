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

        builder.Property(b => b.Currency).HasMaxLength(3);
        builder.Property(b => b.PayPalOrderId).HasMaxLength(64);
        builder.Property(b => b.PayPalOrderStatus).HasMaxLength(32);
        builder.Property(b => b.PayPalAuthorizationId).HasMaxLength(64);
        builder.Property(b => b.PayPalAuthorizationStatus).HasMaxLength(32);
        builder.Property(b => b.PayPalCaptureId).HasMaxLength(64);
        builder.Property(b => b.PayPalCaptureStatus).HasMaxLength(32);
        builder.Property(b => b.CreateOrderRequestId).HasMaxLength(64);
        builder.Property(b => b.AuthorizeRequestId).HasMaxLength(64);
        builder.Property(b => b.ReauthorizeRequestId).HasMaxLength(64);
        builder.Property(b => b.CaptureRequestId).HasMaxLength(64);
        builder.Property(b => b.VoidRequestId).HasMaxLength(64);
        builder.Property(b => b.RowVersion).IsRowVersion();
        builder.Property(b => b.CapturedAmount).HasColumnType("decimal(18,2)");
        builder.Property(b => b.PayPalFee).HasColumnType("decimal(18,2)");
        builder.Property(b => b.NetProceeds).HasColumnType("decimal(18,2)");

        builder.HasMany(o => o.Refunds)
            .WithOne()
            .HasForeignKey(r => r.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

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
