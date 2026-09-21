using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedCardConfiguration : IEntityTypeConfiguration<SavedCard>
{
    public void Configure(EntityTypeBuilder<SavedCard> builder)
    {
        builder.Property(c => c.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(c => c.PayPalVaultId).IsRequired().HasMaxLength(255);
        builder.Property(c => c.PayPalCustomerId).HasMaxLength(64);
        builder.Property(c => c.Brand).HasMaxLength(40);
        builder.Property(c => c.Last4).HasMaxLength(4);
        builder.Property(c => c.Expiry).HasMaxLength(7);

        // A vault token appears at most once per shopper.
        builder.HasIndex(c => new { c.BuyerId, c.PayPalVaultId }).IsUnique();
    }
}
