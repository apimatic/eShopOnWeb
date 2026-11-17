using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        var itemsNavigation = builder.Metadata.FindNavigation(nameof(Order.OrderItems));
        itemsNavigation?.SetPropertyAccessMode(PropertyAccessMode.Field);

        var refundsNavigation = builder.Metadata.FindNavigation(nameof(Order.Refunds));
        refundsNavigation?.SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(b => b.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(o => o.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(o => o.Currency)
            .HasMaxLength(3);

        builder.Property(o => o.PaymentIdempotencyKey)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(o => o.PaymentSourceId).HasMaxLength(64);
        builder.Property(o => o.PaymentCardBrand).HasMaxLength(64);
        builder.Property(o => o.PaymentCardLast4).HasMaxLength(4);
        builder.Property(o => o.AuthorizationId).HasMaxLength(64);
        builder.Property(o => o.AuthorizationStatus).HasMaxLength(32);
        builder.Property(o => o.CaptureTransferId).HasMaxLength(64);
        builder.Property(o => o.CaptureStatus).HasMaxLength(32);
        builder.Property(o => o.PaymentFailureCode).HasMaxLength(64);
        builder.Property(o => o.PaymentFailureMessage).HasMaxLength(1000);
        builder.Property(o => o.FinixFeeCents).HasPrecision(18, 4);
        builder.Property(o => o.NetAmountCents).HasPrecision(18, 4);

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
                .HasMaxLength(60)
                .IsRequired(false);

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
