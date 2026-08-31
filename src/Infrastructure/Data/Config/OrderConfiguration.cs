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

        builder.Property(b => b.PaymentStatus).HasConversion<string>().HasMaxLength(32)
            .HasDefaultValue(OrderPaymentStatus.NotRequired);
        builder.Property(b => b.Currency).HasMaxLength(3).IsRequired();
        builder.Property(b => b.PaymentReference).HasMaxLength(32);
        builder.Property(b => b.PayPalOrderId).HasMaxLength(32);
        builder.Property(b => b.PayPalOrderStatus).HasMaxLength(32);
        builder.Property(b => b.AuthorizationId).HasMaxLength(32);
        builder.Property(b => b.AuthorizationStatus).HasMaxLength(32);
        builder.Property(b => b.CaptureId).HasMaxLength(32);
        builder.Property(b => b.CaptureStatus).HasMaxLength(32);
        builder.Property(b => b.CapturedAmount).HasColumnType("decimal(18,2)");
        builder.Property(b => b.PayPalFee).HasColumnType("decimal(18,2)");
        builder.Property(b => b.NetProceeds).HasColumnType("decimal(18,2)");
        builder.Property(b => b.RefundedAmount).HasColumnType("decimal(18,2)");
        builder.HasIndex(b => b.PayPalOrderId).IsUnique().HasFilter("[PayPalOrderId] IS NOT NULL");
        builder.HasIndex(b => b.PaymentReference).IsUnique().HasFilter("[PaymentReference] IS NOT NULL");
        builder.HasIndex(b => b.AuthorizationId).IsUnique().HasFilter("[AuthorizationId] IS NOT NULL");
        builder.HasIndex(b => b.CaptureId).IsUnique().HasFilter("[CaptureId] IS NOT NULL");
        builder.HasMany(b => b.Refunds)
            .WithOne()
            .HasForeignKey("OrderId")
            .IsRequired()
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
