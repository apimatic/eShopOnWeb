using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentAuthorizationConfiguration : IEntityTypeConfiguration<PaymentAuthorization>
{
    public void Configure(EntityTypeBuilder<PaymentAuthorization> builder)
    {
        builder.ToTable("PaymentAuthorizations");
        builder.Property(x => x.PayPalAuthorizationId).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Amount).HasColumnType("decimal(18,2)");
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.HasIndex(x => x.PayPalAuthorizationId).IsUnique();
        builder.HasIndex(x => x.OrderPaymentId)
            .IsUnique()
            .HasFilter("[IsCurrent] = 1");
    }
}
