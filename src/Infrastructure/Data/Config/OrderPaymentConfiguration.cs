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

        builder.HasIndex(p => p.OrderId).IsUnique();

        builder.Property(p => p.AuthorizationRequestId).IsRequired().HasMaxLength(200);
        builder.Property(p => p.PayPalOrderId).HasMaxLength(50);
        builder.Property(p => p.PayPalAuthorizationId).HasMaxLength(50);
        builder.Property(p => p.AuthorizationStatus).HasMaxLength(50);
        builder.Property(p => p.PayPalCaptureId).HasMaxLength(50);
        builder.Property(p => p.CaptureStatus).HasMaxLength(50);
        builder.Property(p => p.CurrencyCode).IsRequired().HasMaxLength(3);

        builder.HasMany(p => p.Refunds)
            .WithOne()
            .HasForeignKey(r => r.OrderPaymentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
