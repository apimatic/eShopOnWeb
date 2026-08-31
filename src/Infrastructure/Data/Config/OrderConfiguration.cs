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

        builder.Property(x => x.PaymentState).HasConversion<string>().HasMaxLength(32)
            .HasDefaultValue(PaymentState.AwaitingPayment);
        builder.Property(x => x.PaymentReference).IsRequired().HasMaxLength(32)
            .HasDefaultValueSql("LOWER(REPLACE(CONVERT(varchar(36), NEWID()), '-', ''))");
        builder.HasIndex(x => x.PaymentReference).IsUnique();
        builder.Property(x => x.PaymentCurrency).HasMaxLength(3);
        builder.Property(x => x.PayPalOrderId).HasMaxLength(255);
        builder.Property(x => x.PayPalAuthorizationId).HasMaxLength(255);
        builder.Property(x => x.PayPalAuthorizationStatus).HasMaxLength(32);
        builder.Property(x => x.PayPalCaptureId).HasMaxLength(255);
        builder.Property(x => x.PayPalCaptureStatus).HasMaxLength(32);
        builder.Property(x => x.CapturedAmount).HasColumnType("decimal(18,2)");
        builder.Property(x => x.PayPalFee).HasColumnType("decimal(18,2)");
        builder.Property(x => x.NetProceeds).HasColumnType("decimal(18,2)");
        builder.Property(x => x.RefundedAmount).HasColumnType("decimal(18,2)");
        builder.HasIndex(x => x.PayPalOrderId).IsUnique().HasFilter("[PayPalOrderId] IS NOT NULL");
        builder.HasIndex(x => x.PayPalAuthorizationId).IsUnique().HasFilter("[PayPalAuthorizationId] IS NOT NULL");
        builder.HasIndex(x => x.PayPalCaptureId).IsUnique().HasFilter("[PayPalCaptureId] IS NOT NULL");

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
