using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.Property(s => s.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(s => s.VaultTokenId).IsRequired().HasMaxLength(64);
        builder.Property(s => s.Brand).IsRequired().HasMaxLength(32);
        builder.Property(s => s.LastDigits).IsRequired().HasMaxLength(4);
        builder.Property(s => s.Expiry).IsRequired().HasMaxLength(7);
        builder.HasIndex(s => s.VaultTokenId).IsUnique();
    }
}
