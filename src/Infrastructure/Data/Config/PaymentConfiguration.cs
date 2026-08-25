using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.Property(p => p.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(p => p.Currency).IsRequired().HasMaxLength(10);
        builder.Property(p => p.Amount).HasColumnType("decimal(18,4)");
        builder.Property(p => p.CapturedAmount).HasColumnType("decimal(18,4)");
        builder.Property(p => p.PayPalFee).HasColumnType("decimal(18,4)");
        builder.Property(p => p.NetAmount).HasColumnType("decimal(18,4)");
        builder.Property(p => p.PayPalOrderId).HasMaxLength(64);
        builder.Property(p => p.PayPalAuthorizationId).HasMaxLength(64);
        builder.Property(p => p.PayPalCaptureId).HasMaxLength(64);

        // Configure the Refunds collection using field access (DDD pattern matching existing Order entity)
        builder.HasMany(p => p.Refunds)
            .WithOne()
            .HasForeignKey("PaymentId")
            .OnDelete(DeleteBehavior.Cascade);

        var refundsNav = builder.Metadata.FindNavigation(nameof(Payment.Refunds));
        refundsNav?.SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
