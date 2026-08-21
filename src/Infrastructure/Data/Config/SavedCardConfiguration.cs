using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedCardAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedCardConfiguration : IEntityTypeConfiguration<SavedCard>
{
    public void Configure(EntityTypeBuilder<SavedCard> builder)
    {
        builder.Property(c => c.OwnerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(c => c.PayPalVaultId)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(c => c.CardBrand).HasMaxLength(32);
        builder.Property(c => c.LastFourDigits).HasMaxLength(4);
        builder.Property(c => c.Expiry).HasMaxLength(7);

        builder.HasIndex(c => c.OwnerId);
    }
}
