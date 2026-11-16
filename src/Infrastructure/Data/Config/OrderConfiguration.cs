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

        builder.Property(order => order.Currency)
            .IsRequired()
            .HasMaxLength(3);

        builder.Property(order => order.PaymentInstrumentId)
            .HasMaxLength(64);

        builder.Property(order => order.AuthorizationId)
            .HasMaxLength(64);

        builder.Property(order => order.AuthorizationState)
            .HasMaxLength(32);

        builder.Property(order => order.CaptureTransferId)
            .HasMaxLength(64);

        builder.Property(order => order.CaptureTransferState)
            .HasMaxLength(32);

        builder.Property(order => order.PaymentFailureMessage)
            .HasMaxLength(1000);

        builder.HasMany(order => order.Refunds)
            .WithOne()
            .HasForeignKey(refund => refund.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

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
