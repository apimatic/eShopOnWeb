using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderPaymentConfiguration : IEntityTypeConfiguration<OrderPayment>
{
    public void Configure(EntityTypeBuilder<OrderPayment> builder)
    {
        builder.HasKey(p => p.OrderId);
        builder.Property(p => p.OrderId).ValueGeneratedNever();
        builder.Property(p => p.ShopperId).IsRequired().HasMaxLength(256);
        builder.Property(p => p.Currency).IsRequired().HasMaxLength(3);
        builder.Property(p => p.IdempotencyId).IsRequired().HasMaxLength(128);
        builder.Property(p => p.AuthorizationId).HasMaxLength(128);
        builder.Property(p => p.AuthorizationState).HasMaxLength(64);
        builder.Property(p => p.CaptureTransferId).HasMaxLength(128);
        builder.Property(p => p.FailureCode).HasMaxLength(128);
        builder.Property(p => p.FailureMessage).HasMaxLength(1024);
        builder.HasIndex(p => p.AuthorizationId);
        builder.HasIndex(p => p.CaptureTransferId);
    }
}
