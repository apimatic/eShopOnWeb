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
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(o => o.PaymentStatus)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(o => o.Currency)
            .IsRequired()
            .HasMaxLength(16);

        builder.Property(o => o.PaymentAttemptKey)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(o => o.PaymentReference)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(o => o.FinixAuthorizationId).HasMaxLength(128);
        builder.Property(o => o.FinixAuthorizationState).HasMaxLength(64);
        builder.Property(o => o.FinixPaymentInstrumentId).HasMaxLength(128);
        builder.Property(o => o.FinixCaptureTransferId).HasMaxLength(128);
        builder.Property(o => o.FinixCaptureState).HasMaxLength(64);
        builder.Property(o => o.PaymentFailureMessage).HasMaxLength(2048);

        builder.HasIndex(o => o.BuyerId);
        builder.HasIndex(o => o.FinixAuthorizationId);
        builder.HasIndex(o => o.FinixCaptureTransferId);

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
