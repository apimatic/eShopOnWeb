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

        builder.Property(p => p.Currency)
            .IsRequired()
            .HasMaxLength(3);

        builder.Property(p => p.PayPalOrderId)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(p => p.Amount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.CapturedAmount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.PayPalFee).HasColumnType("decimal(18,2)");
        builder.Property(p => p.NetAmount).HasColumnType("decimal(18,2)");

        builder.Property(p => p.Status)
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.HasIndex(p => p.OrderId).IsUnique();

        var refunds = builder.Metadata.FindNavigation(nameof(Payment.Refunds));
        refunds?.SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.OwnsMany(p => p.Refunds, r =>
        {
            r.WithOwner().HasForeignKey("PaymentId");
            r.HasKey("Id");
            r.Property(x => x.PayPalRefundId).IsRequired().HasMaxLength(64);
            r.Property(x => x.Amount).HasColumnType("decimal(18,2)");
            r.Property(x => x.Status).HasMaxLength(30);
            r.Property(x => x.IdempotencyKey).IsRequired().HasMaxLength(128);
        });
    }
}
