using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedCardConfiguration : IEntityTypeConfiguration<SavedCard>
{
    public void Configure(EntityTypeBuilder<SavedCard> builder)
    {
        builder.Property(c => c.BuyerId).HasMaxLength(256).IsRequired();
        builder.Property(c => c.VaultId).HasMaxLength(128).IsRequired();
        builder.Property(c => c.Brand).HasMaxLength(32).IsRequired();
        builder.Property(c => c.LastFourDigits).HasMaxLength(4).IsRequired();
        builder.Property(c => c.ExpiryMonth).HasMaxLength(2);
        builder.Property(c => c.ExpiryYear).HasMaxLength(4);
        builder.Property(c => c.CardholderName).HasMaxLength(256);
    }
}
