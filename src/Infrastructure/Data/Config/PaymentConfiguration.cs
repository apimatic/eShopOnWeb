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

        builder.Property(p => p.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(p => p.CurrencyCode)
            .IsRequired()
            .HasMaxLength(3);

        builder.Property(p => p.InvoiceId)
            .IsRequired()
            .HasMaxLength(127);

        builder.Property(p => p.Amount).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(p => p.CapturedGross).HasColumnType("decimal(18,2)");
        builder.Property(p => p.PayPalFee).HasColumnType("decimal(18,2)");
        builder.Property(p => p.NetAmount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.RefundedAmount).HasColumnType("decimal(18,2)").IsRequired();

        builder.Property(p => p.Status).IsRequired();

        builder.HasIndex(p => p.OrderId).IsUnique();
        builder.HasIndex(p => p.BuyerId);

        builder.HasMany(p => p.Refunds)
            .WithOne()
            .HasForeignKey(r => r.PaymentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
