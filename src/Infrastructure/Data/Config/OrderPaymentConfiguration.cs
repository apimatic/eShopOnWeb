using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderPaymentConfiguration : IEntityTypeConfiguration<OrderPayment>
{
    public void Configure(EntityTypeBuilder<OrderPayment> builder)
    {
        builder.HasIndex(p => p.OrderId).IsUnique();

        builder.Property(p => p.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(p => p.Currency)
            .IsRequired()
            .HasMaxLength(3);

        builder.Property(p => p.PaymentReference)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(p => p.AuthorizationState).HasMaxLength(32);
        builder.Property(p => p.CaptureState).HasMaxLength(32);
        builder.Property(p => p.FailureCode).HasMaxLength(128);
        builder.Property(p => p.FailureMessage).HasMaxLength(1024);
        builder.Property(p => p.CaptureUnavailableReason).HasMaxLength(1024);
        builder.Property(p => p.FinixAuthorizationId).HasMaxLength(128);
        builder.Property(p => p.FinixCaptureTransferId).HasMaxLength(128);
        builder.Property(p => p.FinixPaymentInstrumentId).HasMaxLength(128);
        builder.Property(p => p.CardBrand).HasMaxLength(64);
        builder.Property(p => p.CardLast4).HasMaxLength(4);
    }
}
