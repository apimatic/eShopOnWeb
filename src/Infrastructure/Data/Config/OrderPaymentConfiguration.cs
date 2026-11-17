using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderPaymentConfiguration : IEntityTypeConfiguration<OrderPayment>
{
    public void Configure(EntityTypeBuilder<OrderPayment> builder)
    {
        builder.ToTable("OrderPayments");

        builder.Property(p => p.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(p => p.Currency).IsRequired().HasMaxLength(3);
        builder.Property(p => p.AuthorizationIdempotencyKey).IsRequired().HasMaxLength(128);
        builder.Property(p => p.FinixAuthorizationId).HasMaxLength(64);
        builder.Property(p => p.AuthorizationState).HasMaxLength(32);
        builder.Property(p => p.VoidState).HasMaxLength(32);
        builder.Property(p => p.FinixPaymentInstrumentId).HasMaxLength(64);
        builder.Property(p => p.FinixCaptureTransferId).HasMaxLength(64);
        builder.Property(p => p.CaptureState).HasMaxLength(32);
        builder.Property(p => p.FailureCode).HasMaxLength(64);
        builder.Property(p => p.FailureMessage).HasMaxLength(1000);
        builder.Property(p => p.FinixFeeMinor).HasColumnType("decimal(18,4)");
        builder.Property(p => p.NetAmountMinor).HasColumnType("decimal(18,4)");

        builder.HasIndex(p => p.OrderId).IsUnique();
        builder.HasIndex(p => p.BuyerId);
        builder.HasIndex(p => p.FinixAuthorizationId);
        builder.HasIndex(p => p.FinixCaptureTransferId);
    }
}
