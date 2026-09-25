using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.Property(p => p.PublicId).HasMaxLength(64).IsRequired();
        builder.HasIndex(p => p.PublicId).IsUnique();
        builder.Property(p => p.BuyerId).HasMaxLength(256).IsRequired();
        builder.Property(p => p.PayPalVaultId).HasMaxLength(128).IsRequired();
        builder.Property(p => p.PayPalCustomerId).HasMaxLength(64);
        builder.Property(p => p.CardBrand).HasMaxLength(32);
        builder.Property(p => p.Last4).HasMaxLength(4);
        builder.Property(p => p.Expiry).HasMaxLength(7);
        builder.Property(p => p.CardHolderName).HasMaxLength(300);
    }
}
