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

        builder.Property(o => o.PaymentReference).HasDefaultValueSql("NEWID()");
        builder.HasIndex(o => o.PaymentReference).IsUnique();
        builder.HasIndex(o => o.PayPalOrderId).IsUnique().HasFilter("[PayPalOrderId] IS NOT NULL");
        builder.HasIndex(o => o.PayPalCaptureId).IsUnique().HasFilter("[PayPalCaptureId] IS NOT NULL");

        builder.Property(b => b.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(o => o.PaymentCurrency).HasMaxLength(3);
        builder.Property(o => o.PayPalOrderId).HasMaxLength(64);
        builder.Property(o => o.PayPalAuthorizationId).HasMaxLength(64);
        builder.Property(o => o.PayPalAuthorizationStatus).HasMaxLength(32);
        builder.Property(o => o.AuthorizationRequestId).HasMaxLength(36);
        builder.Property(o => o.ReauthorizationRequestId).HasMaxLength(36);
        builder.Property(o => o.CaptureRequestId).HasMaxLength(36);
        builder.Property(o => o.PayPalCaptureId).HasMaxLength(64);
        builder.Property(o => o.PayPalCaptureStatus).HasMaxLength(32);
        builder.Property(o => o.VoidRequestId).HasMaxLength(36);

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

        builder.HasMany(o => o.Refunds)
            .WithOne()
            .HasForeignKey("OrderId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(o => o.CapturedAmount).HasColumnType("decimal(18,2)");
        builder.Property(o => o.PayPalFee).HasColumnType("decimal(18,2)");
        builder.Property(o => o.NetProceeds).HasColumnType("decimal(18,2)");
    }
}
