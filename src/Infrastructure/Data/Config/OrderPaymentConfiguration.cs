using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderPaymentConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        // Payment state columns added additively to Order
        builder.Property(o => o.PaymentStatus)
            .IsRequired();

        builder.Property(o => o.PayPalOrderId)
            .HasMaxLength(64);

        builder.Property(o => o.PayPalAuthorizationId)
            .HasMaxLength(64);

        builder.Property(o => o.PayPalCaptureId)
            .HasMaxLength(64);

        builder.Property(o => o.CapturedAmount)
            .HasColumnType("decimal(18,2)");

        builder.Property(o => o.PayPalFeeAmount)
            .HasColumnType("decimal(18,2)");

        builder.Property(o => o.RefundedAmount)
            .HasColumnType("decimal(18,2)");

        builder.HasMany(o => o.Refunds)
            .WithOne()
            .HasForeignKey(r => r.OrderId);

        var refundsNav = builder.Metadata.FindNavigation(nameof(Order.Refunds));
        refundsNav?.SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
