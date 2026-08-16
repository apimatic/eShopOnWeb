using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.Property(p => p.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(p => p.CurrencyCode).IsRequired().HasMaxLength(3);
        builder.Property(p => p.CustomId).IsRequired().HasMaxLength(127);
        builder.Property(p => p.Amount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.CapturedAmount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.PayPalFee).HasColumnType("decimal(18,2)");
        builder.Property(p => p.NetAmount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(p => p.OrderId).IsUnique();
        builder.HasIndex(p => p.BuyerId);

        // Refunds are part of the Payment aggregate; model them as an owned collection so they load with it.
        builder.OwnsMany(p => p.Refunds, r =>
        {
            r.WithOwner().HasForeignKey("PaymentId");
            r.HasKey(x => x.Id);
            r.Property(x => x.Id).ValueGeneratedNever();
            r.Property(x => x.IdempotencyKey).IsRequired().HasMaxLength(255);
            r.Property(x => x.Status).HasMaxLength(40);
            r.Property(x => x.Amount).HasColumnType("decimal(18,2)");
        });

        builder.Metadata.FindNavigation(nameof(Payment.Refunds))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
