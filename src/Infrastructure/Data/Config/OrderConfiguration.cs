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
        builder.Property(b => b.PaymentReference).IsRequired().HasMaxLength(32);
        builder.HasIndex(b => b.PaymentReference).IsUnique();

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

        builder.Property(x => x.PaymentStatus).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.FulfilmentStatus).HasConversion<string>().HasMaxLength(32);

        builder.OwnsOne(x => x.Payment, payment =>
        {
            payment.ToTable("OrderPayments");
            payment.Property(x => x.PayPalOrderId).HasMaxLength(64).IsRequired();
            payment.Property(x => x.AuthorizationId).HasMaxLength(64).IsRequired();
            payment.Property(x => x.AuthorizationStatus).HasMaxLength(32).IsRequired();
            payment.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            payment.Property(x => x.AuthorizedAmount).HasPrecision(18, 2);
            payment.Property(x => x.CapturedAmount).HasPrecision(18, 2);
            payment.Property(x => x.PayPalFee).HasPrecision(18, 2);
            payment.Property(x => x.NetAmount).HasPrecision(18, 2);
            payment.Property(x => x.CaptureId).HasMaxLength(64);
            payment.Property(x => x.CaptureStatus).HasMaxLength(32);

            payment.OwnsMany(x => x.Refunds, refund =>
            {
                refund.ToTable("PaymentRefunds");
                refund.WithOwner().HasForeignKey("OrderId");
                refund.HasKey(x => x.Id);
                refund.Property(x => x.Id).ValueGeneratedOnAdd();
                refund.Property(x => x.IdempotencyKey).HasMaxLength(108).IsRequired();
                refund.Property(x => x.PayPalRefundId).HasMaxLength(64).IsRequired();
                refund.Property(x => x.Status).HasMaxLength(32).IsRequired();
                refund.Property(x => x.Amount).HasPrecision(18, 2);
                refund.HasIndex("OrderId", nameof(PaymentRefund.IdempotencyKey)).IsUnique();
            });
            payment.Navigation(x => x.Refunds).UsePropertyAccessMode(PropertyAccessMode.Field);
        });
    }
}
