using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.HasIndex(p => p.PayPalVaultId).IsUnique();
        builder.HasIndex(p => new { p.BuyerId, p.Id });
        builder.Property(p => p.BuyerId).HasMaxLength(256).IsRequired();
        builder.Property(p => p.PayPalVaultId).HasMaxLength(255).IsRequired();
        builder.Property(p => p.PayPalCustomerId).HasMaxLength(64);
        builder.Property(p => p.Brand).HasMaxLength(32).IsRequired();
        builder.Property(p => p.LastFour).HasMaxLength(4).IsRequired();
        builder.Property(p => p.Expiry).HasMaxLength(7).IsRequired();
    }
}
