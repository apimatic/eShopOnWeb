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

        builder.Property(o => o.PaymentStatus)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(o => o.FulfilmentStatus)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(o => o.PaymentCurrency).HasMaxLength(3);
        builder.Property(o => o.PaymentAttemptId).HasMaxLength(64);
        builder.Property(o => o.FinixAuthorizationId).HasMaxLength(64);
        builder.Property(o => o.FinixAuthorizationState).HasMaxLength(32);
        builder.Property(o => o.FinixVoidState).HasMaxLength(32);
        builder.Property(o => o.FinixTransferId).HasMaxLength(64);
        builder.Property(o => o.FinixTransferState).HasMaxLength(32);
        builder.Property(o => o.PaymentFailureMessage).HasMaxLength(1000);

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
