using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.HasIndex(m => m.BuyerId);

        builder.Property(m => m.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(m => m.PayPalVaultTokenId)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(m => m.CardBrand).HasMaxLength(32);
        builder.Property(m => m.LastDigits).HasMaxLength(4);
        builder.Property(m => m.Expiry).HasMaxLength(7);
        builder.Property(m => m.CardholderName).HasMaxLength(256);
    }
}
