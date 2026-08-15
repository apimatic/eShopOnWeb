using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        // Refunds are part of the payment; access the backing collection field.
        var refundsNavigation = builder.Metadata.FindNavigation(nameof(Payment.Refunds));
        refundsNavigation?.SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(p => p.Refunds)
            .WithOne()
            .HasForeignKey("PaymentId")
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(p => p.Reference).IsRequired().HasMaxLength(127);
        builder.Property(p => p.Currency).IsRequired().HasMaxLength(3);
        builder.Property(p => p.IdempotencyKey).IsRequired().HasMaxLength(120);
        builder.Property(p => p.PayPalOrderId).IsRequired().HasMaxLength(64);
        builder.Property(p => p.AuthorizationId).HasMaxLength(64);
        builder.Property(p => p.AuthorizationStatus).HasMaxLength(30);
        builder.Property(p => p.CaptureId).HasMaxLength(64);
        builder.Property(p => p.CaptureStatus).HasMaxLength(30);
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(30).IsRequired();

        builder.Property(p => p.Amount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.CapturedGross).HasColumnType("decimal(18,2)");
        builder.Property(p => p.PayPalFee).HasColumnType("decimal(18,2)");
        builder.Property(p => p.NetAmount).HasColumnType("decimal(18,2)");
    }
}
