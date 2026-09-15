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
            .HasMaxLength(30)
            .IsRequired();

        // The payment is part of the Order aggregate: an owned entity that loads and saves with it.
        builder.OwnsOne(o => o.Payment, p =>
        {
            p.WithOwner();

            p.Property(x => x.Provider).HasMaxLength(50).IsRequired();
            p.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            p.Property(x => x.Amount).HasColumnType("decimal(18,2)");
            p.Property(x => x.PaymentReference).HasMaxLength(255).IsRequired();
            p.Property(x => x.PaymentMethodDescription).HasMaxLength(100);
            p.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);

            p.Property(x => x.PayPalOrderId).HasMaxLength(64);
            p.Property(x => x.AuthorizationId).HasMaxLength(64);
            p.Property(x => x.AuthorizationStatus).HasMaxLength(30);
            p.Property(x => x.CaptureId).HasMaxLength(64);
            p.Property(x => x.CaptureStatus).HasMaxLength(30);
            p.Property(x => x.CapturedAmount).HasColumnType("decimal(18,2)");
            p.Property(x => x.PayPalFee).HasColumnType("decimal(18,2)");
            p.Property(x => x.NetAmount).HasColumnType("decimal(18,2)");

            p.OwnsMany(x => x.Refunds, r =>
            {
                r.WithOwner();
                r.Property(x => x.RefundId).HasMaxLength(64).IsRequired();
                r.Property(x => x.Amount).HasColumnType("decimal(18,2)");
                r.Property(x => x.Status).HasMaxLength(30);
                r.Property(x => x.IdempotencyKey).HasMaxLength(255).IsRequired();
            });
            p.Navigation(x => x.Refunds).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        builder.Navigation(o => o.Payment).IsRequired(false);
    }
}
