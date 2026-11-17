using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderPaymentConfiguration : IEntityTypeConfiguration<OrderPayment>
{
    public void Configure(EntityTypeBuilder<OrderPayment> builder)
    {
        builder.HasKey(x => x.OrderId);
        builder.Property(x => x.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(x => x.PaymentReference).IsRequired().HasMaxLength(64);
        builder.Property(x => x.Currency).IsRequired().HasMaxLength(8);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.AuthorizationId).HasMaxLength(64);
        builder.Property(x => x.AuthorizationState).HasMaxLength(32);
        builder.Property(x => x.AuthorizationIdempotencyId).HasMaxLength(128);
        builder.Property(x => x.CaptureTransferId).HasMaxLength(64);
        builder.Property(x => x.CaptureState).HasMaxLength(32);
        builder.Property(x => x.FailureCode).HasMaxLength(128);
        builder.Property(x => x.FailureMessage).HasMaxLength(1000);
        builder.HasIndex(x => x.AuthorizationId);
        builder.HasIndex(x => x.CaptureTransferId);
    }
}
