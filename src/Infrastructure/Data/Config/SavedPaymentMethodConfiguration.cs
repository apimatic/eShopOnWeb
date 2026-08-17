using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.Property(pm => pm.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(pm => pm.PayPalVaultId).IsRequired().HasMaxLength(64);
        builder.Property(pm => pm.PayPalCustomerId).HasMaxLength(64);
        builder.Property(pm => pm.Brand).IsRequired().HasMaxLength(30);
        builder.Property(pm => pm.Last4).IsRequired().HasMaxLength(4);
        builder.Property(pm => pm.Expiry).IsRequired().HasMaxLength(7);
        builder.Property(pm => pm.CardholderName).HasMaxLength(128);

        builder.HasIndex(pm => pm.BuyerId);
    }
}
