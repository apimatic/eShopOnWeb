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

        builder.Property(p => p.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(p => p.FinixPaymentInstrumentId).HasMaxLength(255);
        builder.Property(p => p.FinixAuthorizationId).HasMaxLength(255);
        builder.Property(p => p.AuthorizationStatus).HasMaxLength(64);
        builder.Property(p => p.AuthorizationIdempotencyId).HasMaxLength(128);
        builder.Property(p => p.FinixCaptureTransferId).HasMaxLength(255);
        builder.Property(p => p.CaptureStatus).HasMaxLength(64);
        builder.Property(p => p.FailureCode).HasMaxLength(128);
        builder.Property(p => p.FailureMessage).HasMaxLength(1000);

        builder.HasIndex(p => p.OrderId).IsUnique();
        builder.HasIndex(p => p.BuyerId);

        builder.HasMany(p => p.Refunds)
            .WithOne()
            .HasForeignKey(r => r.PaymentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(Payment.Refunds))?
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
