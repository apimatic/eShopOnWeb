using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        var navigation = builder.Metadata.FindNavigation(nameof(Payment.Refunds));
        navigation?.SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(p => p.OrderId).IsUnique();

        builder.Property(p => p.Currency).HasMaxLength(3).IsRequired();
        builder.Property(p => p.Amount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.CapturedAmount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.PayPalFeeAmount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.NetAmount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.RefundedAmount).HasColumnType("decimal(18,2)");

        builder.OwnsMany(p => p.Refunds, r =>
        {
            r.WithOwner();
            r.Property(x => x.IdempotencyKey).HasMaxLength(128).IsRequired();
            r.Property(x => x.PayPalRefundId).HasMaxLength(64).IsRequired();
            r.Property(x => x.Status).HasMaxLength(32).IsRequired();
            r.Property(x => x.Amount).HasColumnType("decimal(18,2)");
        });
    }
}
