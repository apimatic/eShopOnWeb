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

        builder.Navigation(x => x.ShipToAddress).IsRequired();

        builder.Property(o => o.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.OwnsOne(o => o.Payment, p =>
        {
            p.WithOwner();
            p.Property(x => x.PayPalOrderId).HasMaxLength(64);
            p.Property(x => x.PayPalRequestId).HasMaxLength(108);
            p.Property(x => x.AuthorizationId).HasMaxLength(64);
            p.Property(x => x.AuthorizationStatus).HasMaxLength(32);
            p.Property(x => x.CaptureId).HasMaxLength(64);
            p.Property(x => x.CaptureStatus).HasMaxLength(32);
            p.Property(x => x.Currency).HasMaxLength(3);
            p.Property(x => x.FulfilRequestId).HasMaxLength(108);
            p.Property(x => x.CancelRequestId).HasMaxLength(108);
            p.Property(x => x.CapturedAmount).HasColumnType("decimal(18,2)");
            p.Property(x => x.PaypalFee).HasColumnType("decimal(18,2)");
            p.Property(x => x.NetProceeds).HasColumnType("decimal(18,2)");
        });
        builder.Navigation(o => o.Payment).IsRequired(false);

        builder.HasMany(o => o.Refunds)
            .WithOne()
            .HasForeignKey(r => r.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        var refunds = builder.Metadata.FindNavigation(nameof(Order.Refunds));
        refunds?.SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
