using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedCardConfiguration : IEntityTypeConfiguration<SavedCard>
{
    public void Configure(EntityTypeBuilder<SavedCard> builder)
    {
        // A shopper saving the same vaulted card twice is deduped by this claim.
        builder.HasIndex(c => new { c.BuyerId, c.PayPalVaultId }).IsUnique();

        builder.Property(c => c.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(c => c.PayPalVaultId).IsRequired().HasMaxLength(255);
        builder.Property(c => c.Brand).HasMaxLength(50);
        builder.Property(c => c.LastDigits).HasMaxLength(4);
        builder.Property(c => c.Expiry).HasMaxLength(7);
        builder.Property(c => c.CardholderName).HasMaxLength(300);
    }
}
