using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.Property(p => p.BuyerId).HasMaxLength(256).IsRequired();
        builder.Property(p => p.PayPalVaultTokenId).HasMaxLength(64).IsRequired();
        builder.Property(p => p.PayPalCustomerId).HasMaxLength(64).IsRequired();
        builder.Property(p => p.CardBrand).HasMaxLength(40);
        builder.Property(p => p.CardLast4).HasMaxLength(4);
        builder.Property(p => p.CardExpiry).HasMaxLength(7);
        builder.Property(p => p.Alias).HasMaxLength(100);

        builder.HasIndex(p => p.BuyerId);
    }
}
