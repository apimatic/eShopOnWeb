using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.Metadata.FindNavigation(nameof(Order.OrderItems))?.SetPropertyAccessMode(PropertyAccessMode.Field);
        builder.Metadata.FindNavigation(nameof(Order.Refunds))?.SetPropertyAccessMode(PropertyAccessMode.Field);
        builder.Property(b => b.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(b => b.PaymentState).HasConversion<string>().HasMaxLength(32)
            .HasDefaultValue(OrderPaymentState.AwaitingPayment);
        builder.Property(b => b.CapturedAmount).HasColumnType("decimal(18,2)");
        builder.Property(b => b.PayPalFee).HasColumnType("decimal(18,2)");
        builder.Property(b => b.NetProceeds).HasColumnType("decimal(18,2)");
        builder.Property(b => b.RefundedAmount).HasColumnType("decimal(18,2)");
        builder.Property(b => b.PayPalOrderId).HasMaxLength(128);
        builder.Property(b => b.AuthorizationId).HasMaxLength(128);
        builder.Property(b => b.CaptureId).HasMaxLength(128);
        builder.HasMany(o => o.Refunds).WithOne().HasForeignKey(r => r.OrderId).OnDelete(DeleteBehavior.Cascade);
        builder.OwnsOne(o => o.ShipToAddress, a =>
        {
            a.WithOwner();
            a.Property(x => x.ZipCode).HasMaxLength(18).IsRequired();
            a.Property(x => x.Street).HasMaxLength(180).IsRequired();
            a.Property(x => x.State).HasMaxLength(60).IsRequired(false);
            a.Property(x => x.Country).HasMaxLength(90).IsRequired();
            a.Property(x => x.City).HasMaxLength(100).IsRequired();
        });
        builder.Navigation(x => x.ShipToAddress).IsRequired();
    }
}
