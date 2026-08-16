using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedCardAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedCardConfiguration : IEntityTypeConfiguration<SavedCard>
{
    public void Configure(EntityTypeBuilder<SavedCard> builder)
    {
        builder.ToTable("SavedCards");

        builder.Property(c => c.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(c => c.VaultTokenId).IsRequired().HasMaxLength(64);
        builder.Property(c => c.PayPalCustomerId).HasMaxLength(64);
        builder.Property(c => c.Brand).IsRequired().HasMaxLength(30);
        builder.Property(c => c.Last4).IsRequired().HasMaxLength(4);
        builder.Property(c => c.Expiry).IsRequired().HasMaxLength(7);
        builder.Property(c => c.Label).HasMaxLength(100);

        builder.HasIndex(c => c.BuyerId);
    }
}
