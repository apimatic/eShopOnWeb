using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.Property(m => m.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(m => m.PayPalVaultId).IsRequired().HasMaxLength(255);
        builder.Property(m => m.PayPalCustomerId).IsRequired().HasMaxLength(64);
        builder.Property(m => m.Brand).HasMaxLength(32);
        builder.Property(m => m.LastDigits).HasMaxLength(4);
        builder.Property(m => m.ExpiryYearMonth).HasMaxLength(7);
        builder.Property(m => m.CardHolderName).HasMaxLength(300);

        builder.HasIndex(m => m.BuyerId);
    }
}
