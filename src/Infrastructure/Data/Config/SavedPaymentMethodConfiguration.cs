using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.Property(pm => pm.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        // PayPal vault token id and customer id (spec: vault_id / merchant_partner_customer_id).
        builder.Property(pm => pm.PayPalVaultId)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(pm => pm.PayPalCustomerId)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(pm => pm.CardBrand).HasMaxLength(50);
        builder.Property(pm => pm.LastFourDigits).HasMaxLength(4);
        builder.Property(pm => pm.CardExpiry).HasMaxLength(7);   // YYYY-MM
        builder.Property(pm => pm.CardholderName).HasMaxLength(300);

        builder.HasIndex(pm => pm.BuyerId);
    }
}
