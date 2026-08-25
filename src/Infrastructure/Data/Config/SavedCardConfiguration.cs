using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.Payment;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedCardConfiguration : IEntityTypeConfiguration<SavedCard>
{
    public void Configure(EntityTypeBuilder<SavedCard> builder)
    {
        builder.Property(s => s.BuyerId).HasMaxLength(256).IsRequired();
        builder.Property(s => s.VaultTokenId).HasMaxLength(100).IsRequired();
        builder.Property(s => s.LastFourDigits).HasMaxLength(4);
        builder.Property(s => s.CardBrand).HasMaxLength(50);
        builder.Property(s => s.Expiry).HasMaxLength(7);
        builder.Property(s => s.CardType).HasMaxLength(30);
    }
}
