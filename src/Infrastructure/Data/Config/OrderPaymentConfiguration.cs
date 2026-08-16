using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderPaymentConfiguration : IEntityTypeConfiguration<OrderPayment>
{
    public void Configure(EntityTypeBuilder<OrderPayment> builder)
    {
        builder.Property(p => p.OrderId).IsRequired();

        builder.Property(p => p.Provider).HasMaxLength(50).IsRequired();
        builder.Property(p => p.CurrencyCode).HasMaxLength(3).IsRequired();
        builder.Property(p => p.PayPalOrderId).HasMaxLength(64).IsRequired();
        builder.Property(p => p.InvoiceId).HasMaxLength(127);
        builder.Property(p => p.RequestId).HasMaxLength(108);

        builder.Property(p => p.CardBrand).HasMaxLength(32);
        builder.Property(p => p.CardLast4).HasMaxLength(4);

        builder.Property(p => p.AuthorizationId).HasMaxLength(64);
        builder.Property(p => p.AuthorizationStatus).HasMaxLength(32);
        builder.Property(p => p.CaptureId).HasMaxLength(64);
        builder.Property(p => p.CaptureStatus).HasMaxLength(32);

        builder.Property(p => p.Amount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.CapturedAmount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.PayPalFee).HasColumnType("decimal(18,2)");
        builder.Property(p => p.NetAmount).HasColumnType("decimal(18,2)");

        builder.HasIndex(p => p.OrderId).IsUnique();

        builder.OwnsMany(p => p.Refunds, refund =>
        {
            refund.WithOwner();
            refund.Property(r => r.PayPalRefundId).HasMaxLength(64).IsRequired();
            refund.Property(r => r.Status).HasMaxLength(32).IsRequired();
            refund.Property(r => r.IdempotencyKey).HasMaxLength(128).IsRequired();
            refund.Property(r => r.Amount).HasColumnType("decimal(18,2)");
        });

        var refundsNavigation = builder.Metadata.FindNavigation(nameof(OrderPayment.Refunds));
        refundsNavigation?.SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
