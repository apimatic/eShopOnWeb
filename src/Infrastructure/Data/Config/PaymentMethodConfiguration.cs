using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentMethodConfiguration : IEntityTypeConfiguration<PaymentMethod>
{
    public void Configure(EntityTypeBuilder<PaymentMethod> builder)
    {
        builder.ToTable("PaymentMethods");

        builder.Property(pm => pm.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(pm => pm.PayPalVaultId).IsRequired().HasMaxLength(64);
        builder.Property(pm => pm.PayPalCustomerId).HasMaxLength(64);
        builder.Property(pm => pm.CardBrand).HasMaxLength(32);
        builder.Property(pm => pm.CardLastFour).HasMaxLength(4);
        builder.Property(pm => pm.CardExpiry).HasMaxLength(7);
        builder.Property(pm => pm.CardholderName).HasMaxLength(256);

        builder.HasIndex(pm => pm.BuyerId);
    }
}
