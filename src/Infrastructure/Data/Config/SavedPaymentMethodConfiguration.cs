using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.Property(pm => pm.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(pm => pm.PayPalVaultId).IsRequired().HasMaxLength(255);
        builder.Property(pm => pm.PayPalCustomerId).IsRequired().HasMaxLength(255);
        builder.Property(pm => pm.CardBrand).HasMaxLength(32);
        builder.Property(pm => pm.CardLast4).HasMaxLength(4);
        builder.Property(pm => pm.CardExpiry).HasMaxLength(7);
        builder.Property(pm => pm.Label).HasMaxLength(128);

        builder.HasIndex(pm => pm.BuyerId);
    }
}
