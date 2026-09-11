using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.ToTable("SavedPaymentMethods");

        builder.Property(pm => pm.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(pm => pm.VaultId).IsRequired().HasMaxLength(255);
        builder.Property(pm => pm.CustomerId).HasMaxLength(255);
        builder.Property(pm => pm.CardBrand).IsRequired().HasMaxLength(50);
        builder.Property(pm => pm.LastFourDigits).IsRequired().HasMaxLength(4);
        builder.Property(pm => pm.Expiry).HasMaxLength(7);
        builder.Property(pm => pm.CardholderName).HasMaxLength(300);

        builder.HasIndex(pm => pm.BuyerId);
    }
}
