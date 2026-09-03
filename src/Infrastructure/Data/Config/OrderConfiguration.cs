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

        builder.Property(b => b.PaymentReference)
            .IsRequired()
            .HasMaxLength(32);

        builder.Ignore(b => b.PayPalInvoiceId);

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

        // ---- PayPal payment / fulfilment state (additive) ----

        builder.Property(o => o.PaymentStatus)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(o => o.Currency).HasMaxLength(3);
        builder.Property(o => o.PayPalOrderId).HasMaxLength(64);
        builder.Property(o => o.AuthorizationId).HasMaxLength(64);
        builder.Property(o => o.AuthorizationStatus).HasMaxLength(32);
        builder.Property(o => o.CaptureId).HasMaxLength(64);
        builder.Property(o => o.CaptureStatus).HasMaxLength(32);

        builder.Property(o => o.CapturedAmount).HasColumnType("decimal(18,2)");
        builder.Property(o => o.PayPalFee).HasColumnType("decimal(18,2)");
        builder.Property(o => o.NetAmount).HasColumnType("decimal(18,2)");

        var refunds = builder.Metadata.FindNavigation(nameof(Order.Refunds));
        refunds?.SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.OwnsMany(o => o.Refunds, r =>
        {
            r.WithOwner();
            r.Property(x => x.PayPalRefundId).HasMaxLength(64).IsRequired();
            r.Property(x => x.Status).HasMaxLength(32);
            r.Property(x => x.IdempotencyKey).HasMaxLength(128).IsRequired();
            r.Property(x => x.Amount).HasColumnType("decimal(18,2)");
        });
    }
}
