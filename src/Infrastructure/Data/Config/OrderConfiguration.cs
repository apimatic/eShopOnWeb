using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

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

        builder.Property(order => order.Status)
            .IsRequired()
            .HasMaxLength(64)
            .HasDefaultValue(OrderStatuses.AwaitingPayment);

        builder.Property(order => order.AuthorizationIdempotencyKey)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(order => order.Currency)
            .HasMaxLength(8);

        builder.Property(order => order.AuthorizationId)
            .HasMaxLength(128);

        builder.Property(order => order.AuthorizationState)
            .HasMaxLength(64);

        builder.Property(order => order.CaptureTransferId)
            .HasMaxLength(128);

        builder.Property(order => order.CaptureState)
            .HasMaxLength(64);

        builder.Property(order => order.PaymentInstrumentId)
            .HasMaxLength(128);

        builder.Property(order => order.CancellationReason)
            .HasMaxLength(512);

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
