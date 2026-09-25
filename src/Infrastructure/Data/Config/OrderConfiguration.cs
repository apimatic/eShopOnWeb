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

        builder.Property(o => o.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        // Optimistic concurrency: the second racing payment write fails on SQL Server.
        builder.Property(o => o.ConcurrencyToken)
            .IsConcurrencyToken();

        // The PayPal-owned payment state, as columns on the Orders table.
        builder.OwnsOne(o => o.Payment, p =>
        {
            p.WithOwner();
            p.Property(x => x.Currency).HasMaxLength(3);
            p.Property(x => x.Amount).HasColumnType("decimal(18,2)");
            p.Property(x => x.PayPalOrderId).HasMaxLength(64);
            p.Property(x => x.AuthorizationId).HasMaxLength(64);
            p.Property(x => x.AuthorizationStatus).HasMaxLength(32);
            p.Property(x => x.CaptureId).HasMaxLength(64);
            p.Property(x => x.CaptureStatus).HasMaxLength(32);
            p.Property(x => x.CapturedGross).HasColumnType("decimal(18,2)");
            p.Property(x => x.PaypalFee).HasColumnType("decimal(18,2)");
            p.Property(x => x.NetAmount).HasColumnType("decimal(18,2)");
        });
        builder.Navigation(o => o.Payment).IsRequired(false);

        // Refunds as an owned collection, keyed unique per order on the caller idempotency key so
        // a repeated refund request cannot refund twice (SQL Server enforces the unique index).
        builder.OwnsMany(o => o.Refunds, r =>
        {
            r.WithOwner().HasForeignKey("OrderId");
            r.Property<int>("Id");
            r.HasKey("Id");
            r.Property(x => x.IdempotencyKey).HasMaxLength(128).IsRequired();
            r.Property(x => x.PayPalRefundId).HasMaxLength(64);
            r.Property(x => x.Amount).HasColumnType("decimal(18,2)");
            r.Property(x => x.Status).HasMaxLength(32);
            r.HasIndex("OrderId", nameof(OrderRefund.IdempotencyKey)).IsUnique();
        });

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
