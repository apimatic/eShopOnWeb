using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.Property(p => p.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(p => p.CurrencyCode)
            .IsRequired()
            .HasMaxLength(3);

        builder.Property(p => p.InvoiceId)
            .IsRequired()
            .HasMaxLength(80);

        builder.Property(p => p.Amount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.CapturedAmount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.PayPalFee).HasColumnType("decimal(18,2)");
        builder.Property(p => p.NetAmount).HasColumnType("decimal(18,2)");

        builder.OwnsMany(p => p.Refunds, refund =>
        {
            refund.WithOwner();
            refund.HasKey(r => r.Id);
            refund.Property(r => r.Id).HasMaxLength(32);
            refund.Property(r => r.IdempotencyKey).IsRequired().HasMaxLength(256);
            refund.Property(r => r.PayPalRefundId).IsRequired().HasMaxLength(256);
            refund.Property(r => r.Status).IsRequired().HasMaxLength(50);
            refund.Property(r => r.Amount).HasColumnType("decimal(18,2)");
        });

        builder.Metadata.FindNavigation(nameof(Payment.Refunds))?
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
