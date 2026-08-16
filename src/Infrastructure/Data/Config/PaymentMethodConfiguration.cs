using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentMethodConfiguration : IEntityTypeConfiguration<PaymentMethod>
{
    public void Configure(EntityTypeBuilder<PaymentMethod> builder)
    {
        builder.Property(pm => pm.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(pm => pm.PayPalVaultTokenId).IsRequired().HasMaxLength(64);
        builder.Property(pm => pm.CardBrand).IsRequired().HasMaxLength(40);
        builder.Property(pm => pm.LastFourDigits).IsRequired().HasMaxLength(4);
        builder.Property(pm => pm.CardholderName).HasMaxLength(200);
        builder.Property(pm => pm.Expiry).HasMaxLength(7);

        builder.HasIndex(pm => pm.BuyerId);
    }
}
