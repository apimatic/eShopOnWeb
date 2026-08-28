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

        builder.Property(b => b.OrderTotal).HasColumnType("decimal(18,2)");
        builder.Property(b => b.AuthorizedAmount).HasColumnType("decimal(18,2)");
        builder.Property(b => b.CapturedAmount).HasColumnType("decimal(18,2)");
        builder.Property(b => b.PayPalFee).HasColumnType("decimal(18,2)");
        builder.Property(b => b.NetProceeds).HasColumnType("decimal(18,2)");
        builder.Property(b => b.Currency).HasMaxLength(3);
        builder.Property(b => b.ConcurrencyToken).IsConcurrencyToken().HasMaxLength(32);
        builder.Property(b => b.PaymentCorrelationId).IsRequired().HasMaxLength(32);
        builder.Property(b => b.PayPalOrderId).HasMaxLength(64);
        builder.Property(b => b.AuthorizationId).HasMaxLength(64);
        builder.Property(b => b.CaptureId).HasMaxLength(64);
        builder.Property(b => b.PayPalOrderStatus).HasMaxLength(32);
        builder.Property(b => b.AuthorizationStatus).HasMaxLength(32);
        builder.Property(b => b.CaptureStatus).HasMaxLength(32);
        builder.Property(b => b.PaymentSource).HasMaxLength(32);
        builder.Property(b => b.PayPalCreateRequestId).HasMaxLength(128);
        builder.Property(b => b.PayPalAuthorizeRequestId).HasMaxLength(128);
        builder.Property(b => b.PayPalCaptureRequestId).HasMaxLength(128);
        builder.Property(b => b.PayPalVoidRequestId).HasMaxLength(128);
        builder.Property(b => b.PayPalReauthorizeRequestId).HasMaxLength(128);
        builder.HasIndex(b => b.PayPalOrderId).IsUnique().HasFilter("[PayPalOrderId] IS NOT NULL");
        builder.HasIndex(b => b.AuthorizationId).IsUnique().HasFilter("[AuthorizationId] IS NOT NULL");
        builder.HasIndex(b => b.CaptureId).IsUnique().HasFilter("[CaptureId] IS NOT NULL");

        var refunds = builder.Metadata.FindNavigation(nameof(Order.Refunds));
        refunds?.SetPropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(o => o.Refunds).WithOne().HasForeignKey("OrderId")
            .IsRequired().OnDelete(DeleteBehavior.Cascade);

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
                .HasMaxLength(60)
                .IsRequired(false);

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
