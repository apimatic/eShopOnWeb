using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderPaymentConfiguration : IEntityTypeConfiguration<OrderPayment>
{
    public void Configure(EntityTypeBuilder<OrderPayment> builder)
    {
        var refundsNav = builder.Metadata.FindNavigation(nameof(OrderPayment.Refunds));
        refundsNav?.SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(p => p.AuthorizationId).HasMaxLength(256);
        builder.Property(p => p.PayPalOrderId).HasMaxLength(256);
        builder.Property(p => p.CaptureId).HasMaxLength(256);
        builder.Property(p => p.CapturedAmount).HasPrecision(18, 2);
        builder.Property(p => p.PayPalFee).HasPrecision(18, 2);
        builder.Property(p => p.NetAmount).HasPrecision(18, 2);
        builder.Property(p => p.TotalRefunded).HasPrecision(18, 2);
    }
}
