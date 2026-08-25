using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedCardConfiguration : IEntityTypeConfiguration<SavedCard>
{
    public void Configure(EntityTypeBuilder<SavedCard> builder)
    {
        builder.ToTable("SavedCards");
        builder.Property(c => c.BuyerId).HasMaxLength(256).IsRequired();
        builder.Property(c => c.PayPalCustomerId).HasMaxLength(100).IsRequired();
        builder.Property(c => c.VaultId).HasMaxLength(100).IsRequired();
        builder.Property(c => c.LastFour).HasMaxLength(4).IsRequired();
        builder.Property(c => c.Brand).HasMaxLength(50).IsRequired();
        builder.Property(c => c.Expiry).HasMaxLength(10).IsRequired();
    }
}
