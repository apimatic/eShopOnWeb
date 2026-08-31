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

        builder.Property(o => o.PaymentCurrency).HasMaxLength(3);
        builder.Property(o => o.PaymentReference).HasMaxLength(32).IsRequired(false);
        builder.Property(o => o.PaypalOrderId).HasMaxLength(64);
        builder.Property(o => o.PaypalOrderStatus).HasMaxLength(32);
        builder.Property(o => o.PaypalAuthorizationId).HasMaxLength(64);
        builder.Property(o => o.PaypalAuthorizationStatus).HasMaxLength(32);
        builder.Property(o => o.PaypalCaptureId).HasMaxLength(64);
        builder.Property(o => o.PaypalCaptureStatus).HasMaxLength(32);
        builder.Property(o => o.AuthorizedAmount).HasColumnType("decimal(18,2)");
        builder.Property(o => o.CapturedAmount).HasColumnType("decimal(18,2)");
        builder.Property(o => o.PaypalFee).HasColumnType("decimal(18,2)");
        builder.Property(o => o.NetProceeds).HasColumnType("decimal(18,2)");
        builder.Property(o => o.RefundedAmount).HasColumnType("decimal(18,2)");

        builder.HasMany(o => o.Refunds)
            .WithOne()
            .HasForeignKey(r => r.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
