using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.Property(pm => pm.BuyerId).HasMaxLength(256).IsRequired();
        builder.Property(pm => pm.PayPalVaultId).HasMaxLength(64).IsRequired();
        builder.Property(pm => pm.PayPalCustomerId).HasMaxLength(64).IsRequired();
        builder.Property(pm => pm.CardBrand).HasMaxLength(40);
        builder.Property(pm => pm.LastFourDigits).HasMaxLength(4);
        builder.Property(pm => pm.CardExpiry).HasMaxLength(7);
        builder.Property(pm => pm.CardholderName).HasMaxLength(100);

        builder.HasIndex(pm => pm.BuyerId);

        // Computed, non-persisted display label.
        builder.Ignore(pm => pm.DisplayName);
    }
}
