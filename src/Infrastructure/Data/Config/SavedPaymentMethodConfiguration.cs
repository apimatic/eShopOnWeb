using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.Property(p => p.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(p => p.PayPalVaultId).IsRequired().HasMaxLength(128);
        builder.Property(p => p.PayPalCustomerId).HasMaxLength(128);
        builder.Property(p => p.Brand).HasMaxLength(40);
        builder.Property(p => p.LastDigits).HasMaxLength(4);
        builder.Property(p => p.Expiry).HasMaxLength(7);
        builder.Property(p => p.CardholderName).HasMaxLength(256);

        builder.HasIndex(p => p.BuyerId);
    }
}
