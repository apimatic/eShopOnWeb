using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        // One row per vault token (SQL Server enforces; in-memory does not).
        builder.HasIndex(m => m.PayPalVaultId).IsUnique();
        builder.HasIndex(m => m.BuyerId);

        builder.Property(m => m.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(m => m.PayPalVaultId).IsRequired().HasMaxLength(255);
        builder.Property(m => m.PayPalCustomerId).HasMaxLength(64);
        builder.Property(m => m.CardBrand).HasMaxLength(32);
        builder.Property(m => m.LastFourDigits).HasMaxLength(4);
        builder.Property(m => m.Expiry).HasMaxLength(7);
        builder.Property(m => m.CardholderName).HasMaxLength(300);
    }
}
