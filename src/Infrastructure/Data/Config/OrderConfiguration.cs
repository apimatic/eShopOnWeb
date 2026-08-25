using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.Metadata.FindNavigation(nameof(Order.OrderItems))
            ?.SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.Metadata.FindNavigation(nameof(Order.Refunds))
            ?.SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(b => b.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(b => b.Status)
            .IsRequired();

        builder.Property(b => b.PayPalOrderId)
            .HasMaxLength(128);

        builder.Property(b => b.AuthorizationId)
            .HasMaxLength(128);

        builder.Property(b => b.CaptureId)
            .HasMaxLength(128);

        builder.Property(b => b.CapturedAmount)
            .HasColumnType("decimal(18,2)");

        builder.Property(b => b.PayPalFee)
            .HasColumnType("decimal(18,2)");

        builder.Property(b => b.NetAmount)
            .HasColumnType("decimal(18,2)");

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
            .HasForeignKey(r => r.OrderId)
            .IsRequired();
    }
}
