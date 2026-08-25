using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        var navigation = builder.Metadata.FindNavigation(nameof(Payment.Refunds));
        navigation?.SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(p => p.PayPalOrderId).IsRequired().HasMaxLength(50);
        builder.Property(p => p.AuthorizationId).IsRequired().HasMaxLength(50);
        builder.Property(p => p.AuthorizationStatus).IsRequired().HasMaxLength(30);
        builder.Property(p => p.Currency).IsRequired().HasMaxLength(3);
        builder.Property(p => p.CaptureId).HasMaxLength(50);
        builder.Property(p => p.CaptureStatus).HasMaxLength(30);

        builder.Property(p => p.AuthorizedAmount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.CapturedAmount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.PayPalFeeAmount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.NetAmount).HasColumnType("decimal(18,2)");
    }
}
