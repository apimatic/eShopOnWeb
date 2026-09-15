using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedCardConfiguration : IEntityTypeConfiguration<SavedCard>
{
    public void Configure(EntityTypeBuilder<SavedCard> builder)
    {
        builder.Property(c => c.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(c => c.VaultTokenId).IsRequired().HasMaxLength(128);
        builder.Property(c => c.CardBrand).HasMaxLength(40);
        builder.Property(c => c.LastFourDigits).HasMaxLength(4);
        builder.Property(c => c.Expiry).HasMaxLength(10);

        builder.HasIndex(c => c.BuyerId);
    }
}
