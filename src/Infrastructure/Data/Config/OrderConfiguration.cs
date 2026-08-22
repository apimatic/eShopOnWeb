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

        builder.Property(o => o.Status)
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(o => o.Currency).HasMaxLength(8);
        builder.Property(o => o.PayPalOrderId).HasMaxLength(64);
        builder.Property(o => o.PayPalAuthorizationId).HasMaxLength(64);
        builder.Property(o => o.PayPalAuthorizationStatus).HasMaxLength(32);
        builder.Property(o => o.PayPalCaptureId).HasMaxLength(64);
        builder.Property(o => o.PayPalCaptureStatus).HasMaxLength(32);
        builder.Property(o => o.AuthorizeRequestId).HasMaxLength(128);
        builder.Property(o => o.CaptureRequestId).HasMaxLength(128);

        builder.Property(o => o.CapturedAmount).HasColumnType("decimal(18,2)");
        builder.Property(o => o.PayPalFee).HasColumnType("decimal(18,2)");
        builder.Property(o => o.NetAmount).HasColumnType("decimal(18,2)");

        builder.HasMany(o => o.Refunds)
            .WithOne()
            .HasForeignKey("OrderId")
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
