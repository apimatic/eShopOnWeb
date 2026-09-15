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

        builder.OwnsOne<OrderPayment>(o => o.Payment, p =>
        {
            p.WithOwner();

            p.Property(x => x.Currency)
                .HasMaxLength(8)
                .IsRequired();

            p.Property(x => x.PayPalOrderId).HasMaxLength(64);
            p.Property(x => x.AuthorizationId).HasMaxLength(64);
            p.Property(x => x.AuthorizationStatus).HasMaxLength(64);
            p.Property(x => x.CaptureId).HasMaxLength(64);
            p.Property(x => x.CaptureStatus).HasMaxLength(64);
            p.Property(x => x.AuthorizeRequestId).HasMaxLength(108);
            p.Property(x => x.CaptureRequestId).HasMaxLength(108);
            p.Property(x => x.PayPalInvoiceId).HasMaxLength(127);

            p.Property(x => x.CapturedAmount).HasColumnType("decimal(18,2)");
            p.Property(x => x.PayPalFee).HasColumnType("decimal(18,2)");
            p.Property(x => x.NetProceeds).HasColumnType("decimal(18,2)");

            p.OwnsMany(x => x.Refunds, refund =>
            {
                refund.ToTable("OrderPaymentRefunds");
                refund.WithOwner().HasForeignKey("OrderId");
                refund.HasKey(r => r.Id);
                refund.Property(r => r.PayPalRefundId).HasMaxLength(64).IsRequired();
                refund.Property(r => r.IdempotencyKey).HasMaxLength(128).IsRequired();
                refund.Property(r => r.Status).HasMaxLength(64).IsRequired();
                refund.Property(r => r.Amount).HasColumnType("decimal(18,2)").IsRequired();
                refund.HasIndex(r => r.IdempotencyKey);
            });

            p.Navigation(x => x.Refunds).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        builder.Navigation(o => o.Payment).IsRequired();
    }
}
