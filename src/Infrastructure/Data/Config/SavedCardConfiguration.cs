using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedCardAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedCardConfiguration : IEntityTypeConfiguration<SavedCard>
{
    public void Configure(EntityTypeBuilder<SavedCard> builder)
    {
        builder.Property(c => c.BuyerId).HasMaxLength(256).IsRequired();
        builder.Property(c => c.PayPalVaultId).HasMaxLength(64).IsRequired();
        builder.Property(c => c.CardBrand).HasMaxLength(40);
        builder.Property(c => c.Last4).HasMaxLength(4);
        builder.Property(c => c.Expiry).HasMaxLength(10);
        builder.Property(c => c.CardholderName).HasMaxLength(256);
    }
}
