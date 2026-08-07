using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.Property(pm => pm.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(pm => pm.PayPalVaultId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(pm => pm.CardBrand)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(pm => pm.Last4)
            .IsRequired()
            .HasMaxLength(4);

        builder.Property(pm => pm.Expiry)
            .HasMaxLength(7); // "YYYY-MM"

        builder.Property(pm => pm.Label)
            .HasMaxLength(100);

        // Scope lookups by owner.
        builder.HasIndex(pm => pm.BuyerId);
    }
}
