using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedCardConfiguration : IEntityTypeConfiguration<SavedCard>
{
    public void Configure(EntityTypeBuilder<SavedCard> builder)
    {
        builder.Property(c => c.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(c => c.VaultTokenId)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(c => c.CardholderName).HasMaxLength(300);
        builder.Property(c => c.Brand).HasMaxLength(64);
        builder.Property(c => c.LastDigits).HasMaxLength(4);
        builder.Property(c => c.Expiry).HasMaxLength(7);
    }
}
