using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        var refundsNavigation = builder.Metadata.FindNavigation(nameof(Payment.Refunds));
        refundsNavigation?.SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(p => p.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(p => p.Provider)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(p => p.CurrencyCode)
            .IsRequired()
            .HasMaxLength(3);

        builder.Property(p => p.Amount)
            .HasColumnType("decimal(18,2)");

        builder.Property(p => p.CapturedGrossAmount)
            .HasColumnType("decimal(18,2)");

        builder.Property(p => p.PayPalFee)
            .HasColumnType("decimal(18,2)");

        builder.Property(p => p.NetAmount)
            .HasColumnType("decimal(18,2)");

        builder.Property(p => p.Status)
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(p => p.IdempotencyRoot)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(p => p.InvoiceId).HasMaxLength(127);
        builder.Property(p => p.PayPalOrderId).HasMaxLength(64);
        builder.Property(p => p.AuthorizationId).HasMaxLength(64);
        builder.Property(p => p.AuthorizationStatus).HasMaxLength(32);
        builder.Property(p => p.CaptureId).HasMaxLength(64);
        builder.Property(p => p.CaptureStatus).HasMaxLength(32);

        builder.HasIndex(p => p.OrderId);
        builder.HasIndex(p => p.BuyerId);

        builder.HasMany(p => p.Refunds)
            .WithOne()
            .HasForeignKey(r => r.PaymentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
