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

        builder.Property(o => o.Status)
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

        // The payment is owned by the order aggregate. It is optional (null until the shopper pays).
        builder.OwnsOne(o => o.Payment, payment =>
        {
            payment.WithOwner();

            payment.Property(p => p.Currency).HasMaxLength(3).IsRequired();
            payment.Property(p => p.InvoiceId).HasMaxLength(127).IsRequired();
            payment.Property(p => p.AuthorizeRequestId).HasMaxLength(64);
            payment.Property(p => p.CaptureRequestId).HasMaxLength(64);
            payment.Property(p => p.AuthorizedAmount).HasColumnType("decimal(18,2)");
            payment.Property(p => p.CapturedAmount).HasColumnType("decimal(18,2)");
            payment.Property(p => p.PayPalFee).HasColumnType("decimal(18,2)");
            payment.Property(p => p.NetAmount).HasColumnType("decimal(18,2)");
            payment.Property(p => p.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
            payment.Property(p => p.PayPalOrderId).HasMaxLength(64);
            payment.Property(p => p.AuthorizationId).HasMaxLength(64);
            payment.Property(p => p.AuthorizationStatus).HasMaxLength(40);
            payment.Property(p => p.CaptureId).HasMaxLength(64);
            payment.Property(p => p.CaptureStatus).HasMaxLength(40);
            payment.Property(p => p.CardBrand).HasMaxLength(40);
            payment.Property(p => p.CardLast4).HasMaxLength(4);

            // Refunds are an owned collection of the payment.
            payment.OwnsMany(p => p.Refunds, refund =>
            {
                refund.WithOwner();
                refund.Property(r => r.IdempotencyKey).HasMaxLength(128).IsRequired();
                refund.Property(r => r.PayPalRefundId).HasMaxLength(64).IsRequired();
                refund.Property(r => r.Amount).HasColumnType("decimal(18,2)");
                refund.Property(r => r.Status).HasMaxLength(40);
            });

            payment.Navigation(p => p.Refunds).UsePropertyAccessMode(PropertyAccessMode.Field);
        });
    }
}
