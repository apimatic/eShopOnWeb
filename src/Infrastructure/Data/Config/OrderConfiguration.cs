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

        builder.Property(o => o.PaymentCurrency)
            .HasMaxLength(3);

        builder.Property(o => o.FinixAuthorizationId)
            .HasMaxLength(256);

        builder.Property(o => o.FinixAuthorizationState)
            .HasMaxLength(64);

        builder.Property(o => o.PendingAuthorizationIdempotencyKey)
            .HasMaxLength(128);

        builder.Property(o => o.FinixPaymentInstrumentId)
            .HasMaxLength(256);

        builder.Property(o => o.FinixCaptureTransferId)
            .HasMaxLength(256);

        builder.Property(o => o.FinixCaptureState)
            .HasMaxLength(64);
    }
}
