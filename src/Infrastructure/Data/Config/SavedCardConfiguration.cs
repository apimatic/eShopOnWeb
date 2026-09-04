using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedCardConfiguration : IEntityTypeConfiguration<SavedCard>
{
    public void Configure(EntityTypeBuilder<SavedCard> builder)
    {
        builder.Property(c => c.BuyerId).HasMaxLength(256).IsRequired();
        builder.Property(c => c.PayPalCustomerId).HasMaxLength(64).IsRequired();
        builder.Property(c => c.PayPalTokenId).HasMaxLength(64).IsRequired();
        builder.Property(c => c.Last4).HasMaxLength(4).IsRequired();
        builder.Property(c => c.Brand).HasMaxLength(32).IsRequired();
        builder.Property(c => c.Expiry).HasMaxLength(8).IsRequired();
        builder.Property(c => c.CardholderName).HasMaxLength(300).IsRequired();
        builder.HasIndex(c => c.BuyerId);
    }
}