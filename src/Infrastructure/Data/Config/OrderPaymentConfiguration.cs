using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderPaymentConfiguration : IEntityTypeConfiguration<OrderPayment>
{
    public void Configure(EntityTypeBuilder<OrderPayment> builder)
    {
        builder.Property(p => p.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(p => p.Currency)
            .IsRequired()
            .HasMaxLength(3);

        builder.Property(p => p.PaymentInstrumentId)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(p => p.AuthorizationId)
            .HasMaxLength(64);

        builder.Property(p => p.AuthorizationState)
            .HasMaxLength(32);

        builder.Property(p => p.AuthorizationIdempotencyKey)
            .HasMaxLength(256);

        builder.Property(p => p.CaptureTransferId)
            .HasMaxLength(64);

        builder.Property(p => p.CaptureTransferState)
            .HasMaxLength(32);

        builder.Property(p => p.FeeCents)
            .HasColumnType("decimal(18,4)");

        builder.Property(p => p.NetCents)
            .HasColumnType("decimal(18,4)");

        builder.Property(p => p.FailureCode)
            .HasMaxLength(64);

        builder.Property(p => p.FailureMessage)
            .HasMaxLength(1000);

        builder.HasIndex(p => p.OrderId).IsUnique();
        builder.HasIndex(p => p.AuthorizationId);
        builder.HasIndex(p => p.CaptureTransferId);
    }
}
