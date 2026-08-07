using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.Property(pm => pm.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.HasIndex(pm => pm.BuyerId);

        builder.Property(pm => pm.VaultId)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(pm => pm.CardBrand)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(pm => pm.LastFourDigits)
            .IsRequired()
            .HasMaxLength(4);

        builder.Property(pm => pm.Expiry)
            .IsRequired()
            .HasMaxLength(7);

        builder.Property(pm => pm.Alias)
            .HasMaxLength(100);
    }
}
