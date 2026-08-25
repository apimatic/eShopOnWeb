using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedCardConfiguration : IEntityTypeConfiguration<SavedCard>
{
    public void Configure(EntityTypeBuilder<SavedCard> builder)
    {
        builder.Property(s => s.ShopperId).IsRequired().HasMaxLength(256);
        builder.Property(s => s.PayPalPaymentTokenId).IsRequired().HasMaxLength(100);
        builder.Property(s => s.PayPalCustomerId).IsRequired().HasMaxLength(100);
        builder.Property(s => s.MerchantCustomerId).IsRequired().HasMaxLength(256);

        builder.HasIndex(s => s.PayPalPaymentTokenId).IsUnique();
    }
}
