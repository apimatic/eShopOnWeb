using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedPaymentMethodAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.Property(m => m.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(m => m.PayPalVaultTokenId).IsRequired().HasMaxLength(100);
        builder.Property(m => m.CardBrand).HasMaxLength(50);
        builder.Property(m => m.Last4).HasMaxLength(4);
        builder.Property(m => m.CardExpiry).HasMaxLength(10);
        builder.Property(m => m.CardholderName).HasMaxLength(256);
    }
}
