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

        builder.HasOne(o => o.Payment)
            .WithOne()
            .HasForeignKey<OrderPayment>(p => p.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(o => o.PaymentStatus).HasConversion<string>().HasMaxLength(32).HasDefaultValue(PaymentStatus.AwaitingPayment);
        builder.Property(o => o.FulfillmentStatus).HasConversion<string>().HasMaxLength(32).HasDefaultValue(FulfillmentStatus.AwaitingPayment);

        builder.Property(b => b.BuyerId)
            .IsRequired()
            .HasMaxLength(256);
        builder.Property(b => b.PaymentReference).HasMaxLength(32).IsRequired().HasDefaultValueSql("LOWER(REPLACE(CONVERT(varchar(36), NEWID()), '-', ''))");
        builder.HasIndex(b => b.PaymentReference).IsUnique();

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
