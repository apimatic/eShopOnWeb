using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderPaymentConfiguration : IEntityTypeConfiguration<OrderPayment>
{
    public void Configure(EntityTypeBuilder<OrderPayment> builder)
    {
        // Refunds are only reached through the aggregate root, via its private backing field.
        var refunds = builder.Metadata.FindNavigation(nameof(OrderPayment.Refunds));
        refunds?.SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(p => p.BuyerId).IsRequired().HasMaxLength(256);
        builder.HasIndex(p => p.OrderId).IsUnique();

        builder.Property(p => p.CurrencyCode).IsRequired().HasMaxLength(3);
        builder.Property(p => p.Amount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.CapturedAmount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.PayPalFee).HasColumnType("decimal(18,2)");
        builder.Property(p => p.NetAmount).HasColumnType("decimal(18,2)");

        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(30).IsRequired();

        builder.Property(p => p.PayPalOrderId).HasMaxLength(64);
        builder.Property(p => p.AuthorizationId).HasMaxLength(64);
        builder.Property(p => p.AuthorizationStatus).HasMaxLength(40);
        builder.Property(p => p.CaptureId).HasMaxLength(64);
        builder.Property(p => p.CaptureStatus).HasMaxLength(40);

        builder.Property(p => p.CreateRequestId).IsRequired().HasMaxLength(64);
        builder.Property(p => p.AuthorizeRequestId).IsRequired().HasMaxLength(64);
        builder.Property(p => p.CaptureRequestId).IsRequired().HasMaxLength(64);
    }
}
