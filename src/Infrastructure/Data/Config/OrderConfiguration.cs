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
        builder.HasMany(order => order.Refunds)
            .WithOne()
            .HasForeignKey(refund => refund.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(b => b.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(order => order.PaymentCurrency).HasMaxLength(3);
        builder.Property(order => order.PaymentReference).IsRequired().HasMaxLength(32)
            .HasDefaultValueSql("LOWER(REPLACE(CONVERT(varchar(36), NEWID()), '-', ''))");
        builder.HasIndex(order => order.PaymentReference).IsUnique();
        builder.Property(order => order.PayPalOrderId).HasMaxLength(32);
        builder.Property(order => order.PayPalOrderStatus).HasMaxLength(32);
        builder.Property(order => order.PayPalAuthorizationId).HasMaxLength(32);
        builder.Property(order => order.PayPalAuthorizationStatus).HasMaxLength(32);
        builder.Property(order => order.PayPalCaptureId).HasMaxLength(32);
        builder.Property(order => order.PayPalCaptureStatus).HasMaxLength(32);
        builder.Property(order => order.CapturedAmount).HasColumnType("decimal(18,2)");
        builder.Property(order => order.PayPalFee).HasColumnType("decimal(18,2)");
        builder.Property(order => order.MerchantNetAmount).HasColumnType("decimal(18,2)");
        builder.Property(order => order.RefundedAmount).HasColumnType("decimal(18,2)");
        builder.HasIndex(order => order.PayPalOrderId).IsUnique().HasFilter("[PayPalOrderId] IS NOT NULL");
        builder.HasIndex(order => order.PayPalAuthorizationId).IsUnique().HasFilter("[PayPalAuthorizationId] IS NOT NULL");
        builder.HasIndex(order => order.PayPalCaptureId).IsUnique().HasFilter("[PayPalCaptureId] IS NOT NULL");

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
