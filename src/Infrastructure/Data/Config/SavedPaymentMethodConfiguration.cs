using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.Property(pm => pm.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(pm => pm.PayPalVaultId).IsRequired().HasMaxLength(255);
        builder.Property(pm => pm.PayPalCustomerId).IsRequired().HasMaxLength(64);
        builder.Property(pm => pm.Brand).HasMaxLength(32);
        builder.Property(pm => pm.Last4).HasMaxLength(4);
        builder.Property(pm => pm.Expiry).HasMaxLength(7);
        builder.Property(pm => pm.CardHolderName).HasMaxLength(300);
    }
}
