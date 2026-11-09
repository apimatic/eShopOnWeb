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

        builder.Property(o => o.PaymentMethodId).HasMaxLength(64);
        builder.Property(o => o.PaymentMethodType).HasMaxLength(16);
        builder.Property(o => o.PaymentTransactionId).HasMaxLength(64);
        builder.Property(o => o.PaymentNote).HasMaxLength(1000);
        builder.Property(o => o.PaymentState).HasConversion<string>().HasMaxLength(16);
        builder.Property(o => o.Fee).HasColumnType("decimal(18,2)");
        builder.Property(o => o.AmountCharged).HasColumnType("decimal(18,2)");
        builder.Property(o => o.RefundState).HasConversion<string>().HasMaxLength(16);
        builder.Property(o => o.RefundTransactionId).HasMaxLength(64);
        builder.Property(o => o.RefundNote).HasMaxLength(1000);
        builder.Ignore(o => o.IsPaid);

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
