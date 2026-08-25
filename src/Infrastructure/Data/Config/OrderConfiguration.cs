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

        builder.Property(o => o.Currency)
            .HasMaxLength(3)
            .IsRequired();

        builder.OwnsOne(o => o.Payment, p =>
        {
            p.WithOwner();

            p.Property(x => x.AuthorizedAmount).HasColumnType("decimal(18,2)");
            p.Property(x => x.CapturedAmount).HasColumnType("decimal(18,2)");
            p.Property(x => x.PayPalFeeAmount).HasColumnType("decimal(18,2)");
            p.Property(x => x.NetAmount).HasColumnType("decimal(18,2)");
            p.Property(x => x.RefundedAmount).HasColumnType("decimal(18,2)");
        });

        var paymentNavigation = builder.Metadata.FindNavigation(nameof(Order.Payment));
        paymentNavigation?.SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.OwnsMany(o => o.Refunds, r =>
        {
            r.WithOwner().HasForeignKey("OrderId");
            r.HasKey(x => x.Id);
            r.Property(x => x.Amount).HasColumnType("decimal(18,2)");
        });

        var refundsNavigation = builder.Metadata.FindNavigation(nameof(Order.Refunds));
        refundsNavigation?.SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
