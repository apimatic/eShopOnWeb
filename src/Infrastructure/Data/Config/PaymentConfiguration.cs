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

        builder.Property(p => p.AuthorizationId)
            .HasMaxLength(64);

        builder.Property(p => p.CaptureTransferId)
            .HasMaxLength(64);

        builder.Property(p => p.CaptureStatus)
            .HasMaxLength(32);

        builder.Property(p => p.FinixFeeCents).HasPrecision(18, 4);
        builder.Property(p => p.NetProceedsCents).HasPrecision(18, 4);

        builder.Property(p => p.FinixPaymentInstrumentId)
            .HasMaxLength(64);

        builder.HasIndex(p => p.AuthorizationId);
        builder.HasIndex(p => p.CaptureTransferId);

        var refunds = builder.Metadata.FindNavigation(nameof(Payment.Refunds));
        refunds?.SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
