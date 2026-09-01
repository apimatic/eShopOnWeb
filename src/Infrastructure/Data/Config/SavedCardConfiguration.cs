using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedCardConfiguration : IEntityTypeConfiguration<SavedCard>
{
    public void Configure(EntityTypeBuilder<SavedCard> builder)
    {
        builder.Property(c => c.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(c => c.PayPalPaymentTokenId)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(c => c.PayPalCustomerId).HasMaxLength(64);
        builder.Property(c => c.Brand).HasMaxLength(32);
        builder.Property(c => c.LastDigits).HasMaxLength(4);
        builder.Property(c => c.Expiry).HasMaxLength(7);

        builder.HasIndex(c => c.BuyerId);
        builder.HasIndex(c => c.PayPalPaymentTokenId).IsUnique();
    }
}
