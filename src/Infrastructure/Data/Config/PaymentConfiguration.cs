using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.Property(p => p.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(p => p.Currency).IsRequired().HasMaxLength(3);
        builder.Property(p => p.PayPalOrderId).IsRequired().HasMaxLength(64);
        builder.Property(p => p.InvoiceId).IsRequired().HasMaxLength(128);

        builder.Property(p => p.Amount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.CapturedAmount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.PayPalFee).HasColumnType("decimal(18,2)");
        builder.Property(p => p.NetAmount).HasColumnType("decimal(18,2)");

        builder.Property(p => p.AuthorizationId).HasMaxLength(64);
        builder.Property(p => p.CaptureId).HasMaxLength(64);
        builder.Property(p => p.CardLast4).HasMaxLength(4);
        builder.Property(p => p.CardBrand).HasMaxLength(32);

        // One payment per order.
        builder.HasIndex(p => p.OrderId).IsUnique();

        builder.OwnsMany(p => p.Refunds, r =>
        {
            r.WithOwner();
            r.Property(x => x.RefundId).IsRequired().HasMaxLength(64);
            r.Property(x => x.IdempotencyKey).IsRequired().HasMaxLength(128);
            r.Property(x => x.Status).HasMaxLength(32);
            r.Property(x => x.Amount).HasColumnType("decimal(18,2)");
        });

        var refundsNav = builder.Metadata.FindNavigation(nameof(Payment.Refunds));
        refundsNav?.SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
