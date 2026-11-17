using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderPaymentConfiguration : IEntityTypeConfiguration<OrderPayment>
{
    public void Configure(EntityTypeBuilder<OrderPayment> builder)
    {
        builder.HasKey(p => p.OrderId);
        builder.Property(p => p.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(p => p.Currency).IsRequired().HasMaxLength(3);
        builder.Property(p => p.AuthorizationId).HasMaxLength(64);
        builder.Property(p => p.CaptureTransferId).HasMaxLength(64);
        builder.Property(p => p.IdempotencyId).HasMaxLength(128);
        builder.Property(p => p.SavedCardId).HasMaxLength(64);
        builder.HasIndex(p => p.BuyerId);
    }
}
