using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderPaymentConfiguration : IEntityTypeConfiguration<OrderPayment>
{
    public void Configure(EntityTypeBuilder<OrderPayment> builder)
    {
        builder.HasKey(p => p.OrderId);
        builder.Property(p => p.BuyerId).HasMaxLength(256).IsRequired();
        builder.Property(p => p.Currency).HasMaxLength(3).IsRequired();
        builder.Property(p => p.Status).HasMaxLength(32).IsRequired();
        builder.Property(p => p.AuthorizationId).HasMaxLength(64);
        builder.Property(p => p.PaymentInstrumentId).HasMaxLength(64);
        builder.Property(p => p.CaptureTransferId).HasMaxLength(64);
        builder.Property(p => p.PaymentIdempotencyKey).HasMaxLength(128);
        builder.HasIndex(p => p.BuyerId);
    }
}
