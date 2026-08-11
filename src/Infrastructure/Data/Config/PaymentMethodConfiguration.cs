using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentMethodConfiguration : IEntityTypeConfiguration<PaymentMethod>
{
    public void Configure(EntityTypeBuilder<PaymentMethod> builder)
    {
        builder.Property(pm => pm.BuyerId).HasMaxLength(256).IsRequired();
        builder.Property(pm => pm.VaultId).HasMaxLength(100).IsRequired();
        builder.Property(pm => pm.PayPalCustomerId).HasMaxLength(100);
        builder.Property(pm => pm.CardBrand).HasMaxLength(50);
        builder.Property(pm => pm.LastFourDigits).HasMaxLength(4).IsRequired();
        builder.Property(pm => pm.CardExpiry).HasMaxLength(7);
        builder.Property(pm => pm.CardholderName).HasMaxLength(100);

        builder.HasIndex(pm => pm.BuyerId);
    }
}
