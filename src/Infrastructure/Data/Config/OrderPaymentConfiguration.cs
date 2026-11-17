using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.Payments;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderPaymentConfiguration : IEntityTypeConfiguration<OrderPayment>
{
    public void Configure(EntityTypeBuilder<OrderPayment> builder)
    {
        builder.HasOne(p => p.Order)
            .WithOne(o => o.Payment)
            .HasForeignKey<OrderPayment>(p => p.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(p => p.OrderId).IsUnique();

        builder.Property(p => p.Currency)
            .IsRequired()
            .HasMaxLength(3);

        builder.Property(p => p.AuthorizationIdempotencyKey)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(p => p.FinixAuthorizationId).HasMaxLength(128);
        builder.Property(p => p.AuthorizationState).HasMaxLength(32);
        builder.Property(p => p.FinixCaptureTransferId).HasMaxLength(128);
        builder.Property(p => p.CaptureState).HasMaxLength(32);
        builder.Property(p => p.PaymentInstrumentId).HasMaxLength(128);
        builder.Property(p => p.LastError).HasMaxLength(2000);

        builder.Metadata
            .FindNavigation(nameof(OrderPayment.Refunds))?
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
