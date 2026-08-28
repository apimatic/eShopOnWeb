using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentAuthorizationConfiguration : IEntityTypeConfiguration<PaymentAuthorization>
{
    public void Configure(EntityTypeBuilder<PaymentAuthorization> builder)
    {
        builder.ToTable("PaymentAuthorizations");
        builder.HasIndex(x => x.ExternalReference).IsUnique();
        builder.HasIndex(x => new { x.OrderPaymentId, x.IsCurrent }).IsUnique()
            .HasFilter("[IsCurrent] = 1");
        builder.HasIndex(x => x.PayPalOrderId).IsUnique().HasFilter("[PayPalOrderId] IS NOT NULL");
        builder.HasIndex(x => x.PayPalAuthorizationId).IsUnique().HasFilter("[PayPalAuthorizationId] IS NOT NULL");
        builder.Property(x => x.SourceType).HasMaxLength(32).IsRequired();
        builder.Property(x => x.ExternalReference).HasMaxLength(64).IsRequired();
        builder.Property(x => x.CreateOrderRequestId).HasMaxLength(108).IsRequired();
        builder.Property(x => x.AuthorizeRequestId).HasMaxLength(108).IsRequired();
        builder.Property(x => x.ReauthorizeRequestId).HasMaxLength(108).IsRequired();
        builder.Property(x => x.PayPalOrderId).HasMaxLength(64);
        builder.Property(x => x.PayPalOrderStatus).HasMaxLength(32);
        builder.Property(x => x.PayPalAuthorizationId).HasMaxLength(64);
        builder.Property(x => x.PayPalAuthorizationStatus).HasMaxLength(32);
        builder.Property(x => x.AuthorizedAmount).HasPrecision(18, 2);
        builder.Property(x => x.Currency).HasMaxLength(3);
    }
}
