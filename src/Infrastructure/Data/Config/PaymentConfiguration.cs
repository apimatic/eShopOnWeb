using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.Property(x => x.Amount).HasColumnType("decimal(18,2)");
        builder.Property(x => x.CapturedAmount).HasColumnType("decimal(18,2)");
        builder.Property(x => x.PayPalFee).HasColumnType("decimal(18,2)");
        builder.Property(x => x.NetAmount).HasColumnType("decimal(18,2)");
        builder.Property(x => x.RefundedAmount).HasColumnType("decimal(18,2)");
        builder.Property(x => x.Currency).IsRequired().HasMaxLength(3);
        builder.Property(x => x.MerchantReference).IsRequired().HasMaxLength(64);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.PayPalOrderId).HasMaxLength(36);
        builder.Property(x => x.PayPalOrderStatus).HasMaxLength(32);
        builder.Property(x => x.AuthorizationId).HasMaxLength(64);
        builder.Property(x => x.AuthorizationStatus).HasMaxLength(32);
        builder.Property(x => x.CaptureId).HasMaxLength(64);
        builder.Property(x => x.CaptureStatus).HasMaxLength(32);
        builder.Property(x => x.ConcurrencyStamp).IsRequired().HasMaxLength(32).IsConcurrencyToken();
        builder.HasIndex(x => x.PayPalOrderId).IsUnique().HasFilter("[PayPalOrderId] IS NOT NULL");
        builder.HasIndex(x => x.MerchantReference).IsUnique();
        builder.HasIndex(x => x.AuthorizationId).IsUnique().HasFilter("[AuthorizationId] IS NOT NULL");
        builder.HasIndex(x => x.CaptureId).IsUnique().HasFilter("[CaptureId] IS NOT NULL");

        var refunds = builder.Metadata.FindNavigation(nameof(Payment.Refunds));
        refunds?.SetPropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(x => x.Refunds)
            .WithOne()
            .HasForeignKey(x => x.PaymentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
