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

        builder.OwnsOne(o => o.Payment, payment =>
        {
            payment.ToTable("OrderPayments");
            payment.WithOwner();

            payment.Property(p => p.PayPalOrderId).HasMaxLength(64).IsRequired();
            payment.Property(p => p.InvoiceId).HasMaxLength(127);
            payment.Property(p => p.Currency).HasMaxLength(3).IsRequired();
            payment.Property(p => p.OriginalAuthorizationId).HasMaxLength(64);
            payment.Property(p => p.AuthorizationId).HasMaxLength(64);
            payment.Property(p => p.AuthorizationStatus).HasMaxLength(32);
            payment.Property(p => p.CaptureId).HasMaxLength(64);
            payment.Property(p => p.CaptureStatus).HasMaxLength(32);
            payment.Property(p => p.CapturedAmount).HasColumnType("decimal(18,2)");
            payment.Property(p => p.PaypalFee).HasColumnType("decimal(18,2)");
            payment.Property(p => p.NetAmount).HasColumnType("decimal(18,2)");

            payment.OwnsMany(p => p.Refunds, refund =>
            {
                refund.ToTable("OrderRefunds");
                refund.WithOwner();
                refund.Property(r => r.PayPalRefundId).HasMaxLength(64).IsRequired();
                refund.Property(r => r.Status).HasMaxLength(32).IsRequired();
                refund.Property(r => r.Amount).HasColumnType("decimal(18,2)").IsRequired();
                refund.Property(r => r.Currency).HasMaxLength(3).IsRequired();
                refund.Property(r => r.IdempotencyKey).HasMaxLength(100).IsRequired();
            });

            payment.Navigation(p => p.Refunds)
                .UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        builder.Navigation(o => o.Payment).IsRequired(false);
    }
}
