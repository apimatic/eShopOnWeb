using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.Property(m => m.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(m => m.PayPalVaultId)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(m => m.LastDigits)
            .IsRequired()
            .HasMaxLength(4);

        builder.Property(m => m.Brand)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(m => m.Expiry).HasMaxLength(7);
        builder.Property(m => m.CardholderName).HasMaxLength(300);

        builder.HasIndex(m => m.BuyerId);
        builder.HasIndex(m => m.PayPalVaultId).IsUnique();
    }
}
