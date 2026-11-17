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
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(o => o.PaymentCurrency)
            .IsRequired()
            .HasMaxLength(3);

        builder.Property(o => o.PaymentIdempotencyKey)
            .HasMaxLength(128);

        builder.Property(o => o.PaymentFailureMessage)
            .HasMaxLength(1000);

        builder.Property(o => o.FinixAuthorizationId)
            .HasMaxLength(64);

        builder.Property(o => o.AuthorizationState)
            .HasMaxLength(32);

        builder.Property(o => o.FinixCaptureId)
            .HasMaxLength(64);

        builder.Property(o => o.CaptureState)
            .HasMaxLength(32);

        builder.Property(o => o.AuthorizedAmount)
            .HasColumnType("decimal(18,2)");

        builder.Property(o => o.CapturedAmount)
            .HasColumnType("decimal(18,2)");

        builder.Property(o => o.FinixFee)
            .HasColumnType("decimal(18,2)");

        builder.Property(o => o.NetProceeds)
            .HasColumnType("decimal(18,2)");

        builder.Property(o => o.RefundedAmount)
            .HasColumnType("decimal(18,2)");

        builder.HasIndex(o => o.BuyerId);
        builder.HasIndex(o => o.FinixAuthorizationId);
        builder.HasIndex(o => o.FinixCaptureId);

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
