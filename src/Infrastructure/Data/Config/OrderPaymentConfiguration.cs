using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderPaymentConfiguration : IEntityTypeConfiguration<OrderPayment>
{
    public void Configure(EntityTypeBuilder<OrderPayment> builder)
    {
        var navigation = builder.Metadata.FindNavigation(nameof(OrderPayment.Refunds));
        navigation?.SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(p => p.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(p => p.PayPalOrderId).IsRequired().HasMaxLength(50);
        builder.Property(p => p.AuthorizationId).HasMaxLength(50);
        builder.Property(p => p.CaptureId).HasMaxLength(50);
        builder.Property(p => p.Currency).IsRequired().HasMaxLength(10);
        builder.Property(p => p.OrderTotal).HasColumnType("decimal(18,2)");
        builder.Property(p => p.CapturedAmount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.PayPalFee).HasColumnType("decimal(18,2)");
        builder.Property(p => p.NetAmount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.CreateIdempotencyKey).IsRequired().HasMaxLength(100);
        builder.Property(p => p.AuthorizeIdempotencyKey).IsRequired().HasMaxLength(100);
        builder.Property(p => p.CaptureIdempotencyKey).HasMaxLength(100);

        builder.HasIndex(p => p.OrderId).IsUnique();
    }
}
