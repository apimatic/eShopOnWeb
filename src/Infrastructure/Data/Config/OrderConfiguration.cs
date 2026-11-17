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

        builder.Property(o => o.Status)
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(o => o.PaymentAuthorizationId).HasMaxLength(64);
        builder.Property(o => o.PaymentAuthorizationState).HasMaxLength(32);
        builder.Property(o => o.PaymentVoidState).HasMaxLength(32);
        builder.Property(o => o.PaymentCaptureTransferId).HasMaxLength(64);
        builder.Property(o => o.PaymentCaptureState).HasMaxLength(32);
        builder.Property(o => o.PaymentCurrency).HasMaxLength(3);
        builder.Property(o => o.PaymentInstrumentId).HasMaxLength(64);
        builder.Property(o => o.PaymentFailureMessage).HasMaxLength(1024);
        builder.Property(o => o.PaymentIdempotencyKey).HasMaxLength(64);

        builder.HasMany(o => o.Refunds)
            .WithOne()
            .HasForeignKey(r => r.OrderId)
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
