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

        builder.Property(b => b.PaymentStatus)
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(b => b.Currency).HasMaxLength(3);
        builder.Property(b => b.AuthorizationState).HasMaxLength(32);
        builder.Property(b => b.CaptureState).HasMaxLength(32);
        builder.Property(b => b.FinixAuthorizationId).HasMaxLength(64);
        builder.Property(b => b.CaptureTransferId).HasMaxLength(64);
        builder.Property(b => b.PaymentFailureCode).HasMaxLength(64);
        builder.Property(b => b.PaymentFailureMessage).HasMaxLength(500);

        builder.Ignore(b => b.RemainingRefundableCents);
        builder.Ignore(b => b.IsAwaitingPayment);
        builder.Ignore(b => b.IsAuthorized);
        builder.Ignore(b => b.IsFulfilled);

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
