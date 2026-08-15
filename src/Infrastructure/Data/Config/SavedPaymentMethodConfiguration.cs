using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.Property(p => p.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(p => p.PayPalVaultId)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(p => p.PayPalCustomerId)
            .HasMaxLength(64);

        builder.Property(p => p.CardBrand)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(p => p.LastFourDigits)
            .IsRequired()
            .HasMaxLength(4);

        builder.Property(p => p.ExpiryYearMonth)
            .HasMaxLength(7);

        builder.Property(p => p.CardholderName)
            .HasMaxLength(128);
    }
}
