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

        builder.Property(o => o.PaymentStatus)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

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

        // PayPal-owned payment state, mapped inline into the Orders table. Optional — null until
        // the order is authorized.
        builder.OwnsOne(o => o.Payment, p =>
        {
            p.WithOwner();
            p.Property(x => x.PayPalOrderId).HasMaxLength(64);
            p.Property(x => x.Reference).HasMaxLength(127);
            p.Property(x => x.AuthorizationId).HasMaxLength(64);
            p.Property(x => x.AuthorizationStatus).HasMaxLength(30);
            p.Property(x => x.CaptureId).HasMaxLength(64);
            p.Property(x => x.CaptureStatus).HasMaxLength(30);
            p.Property(x => x.Currency).HasMaxLength(3);
            p.Property(x => x.CapturedAmount).HasColumnType("decimal(18,2)");
            p.Property(x => x.PayPalFee).HasColumnType("decimal(18,2)");
            p.Property(x => x.NetAmount).HasColumnType("decimal(18,2)");
        });
        builder.Navigation(x => x.Payment).IsRequired(false);

        // Refunds issued against the order's capture — an owned collection (a separate table).
        var refundsNav = builder.Metadata.FindNavigation(nameof(Order.Refunds));
        refundsNav?.SetPropertyAccessMode(PropertyAccessMode.Field);
        builder.OwnsMany(o => o.Refunds, r =>
        {
            r.WithOwner().HasForeignKey("OrderId");
            r.Property(x => x.RefundId).HasMaxLength(64).IsRequired();
            r.Property(x => x.Status).HasMaxLength(30);
            r.Property(x => x.IdempotencyKey).HasMaxLength(128).IsRequired();
            r.Property(x => x.Amount).HasColumnType("decimal(18,2)");
            r.HasKey("OrderId", nameof(OrderRefund.RefundId));
        });
    }
}
