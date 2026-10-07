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

        // Optional: orders placed through the API may not carry a shipping address.
        builder.Navigation(x => x.ShipToAddress).IsRequired(false);

        builder.Property(o => o.PaymentStatus)
            .HasConversion<string>()
            .HasMaxLength(32)
            .HasDefaultValue(OrderPaymentStatus.AwaitingPayment)
            .IsRequired();

        // Rotated on every payment/refund change: two requests deciding on the same version cannot both save.
        builder.Property(o => o.PaymentVersion)
            .IsConcurrencyToken();

        builder.HasMany(o => o.PaymentAttempts)
            .WithOne()
            .HasForeignKey(a => a.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(o => o.PaymentAttempts).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(o => o.Refunds)
            .WithOne()
            .HasForeignKey(r => r.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(o => o.Refunds).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(o => o.PaymentRecord)
            .WithOne()
            .HasForeignKey(e => e.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(o => o.PaymentRecord).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
