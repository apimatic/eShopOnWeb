using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.Property(m => m.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(m => m.PayPalVaultId).IsRequired().HasMaxLength(256);
        builder.Property(m => m.CardBrand).HasMaxLength(50);
        builder.Property(m => m.LastFourDigits).HasMaxLength(4);
        builder.Property(m => m.Expiry).HasMaxLength(7);

        builder.HasIndex(m => m.BuyerId);
    }
}
