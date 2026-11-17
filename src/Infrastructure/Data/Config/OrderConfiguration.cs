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

        builder.Property(o => o.PaymentReference)
            .IsRequired()
            .HasMaxLength(64);

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

        builder.Property(o => o.PaymentStatus)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(o => o.Currency)
            .HasMaxLength(3);

        builder.Property(o => o.FinixAuthorizationId).HasMaxLength(64);
        builder.Property(o => o.FinixAuthorizationState).HasMaxLength(32);
        builder.Property(o => o.FinixPaymentInstrumentId).HasMaxLength(64);
        builder.Property(o => o.FinixTransferId).HasMaxLength(64);
        builder.Property(o => o.PaymentFailureMessage).HasMaxLength(1000);

        builder.Property(o => o.CapturedAmount).HasColumnType("decimal(18,2)");
        builder.Property(o => o.FeeAmount).HasColumnType("decimal(18,2)");
        builder.Property(o => o.NetAmount).HasColumnType("decimal(18,2)");
        builder.Property(o => o.RefundedAmount).HasColumnType("decimal(18,2)");
    }
}
