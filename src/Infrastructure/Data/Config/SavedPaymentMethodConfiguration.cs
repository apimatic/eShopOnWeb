using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.Property(x => x.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(x => x.PayPalCustomerId).IsRequired().HasMaxLength(100);
        builder.Property(x => x.VaultTokenId).IsRequired().HasMaxLength(100);
        builder.Property(x => x.LastDigits).HasMaxLength(10);
        builder.Property(x => x.Brand).HasMaxLength(50);
        builder.Property(x => x.Expiry).HasMaxLength(10);
        builder.HasIndex(x => x.BuyerId);
    }
}
