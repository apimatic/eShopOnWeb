using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.Property(m => m.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(m => m.VaultTokenId)
            .IsRequired()
            .HasMaxLength(64);

        builder.HasIndex(m => m.VaultTokenId).IsUnique();

        builder.Property(m => m.PayPalCustomerId).HasMaxLength(64);
        builder.Property(m => m.Brand).HasMaxLength(32);
        builder.Property(m => m.LastDigits).HasMaxLength(4);
        builder.Property(m => m.Expiry).HasMaxLength(7);
        builder.Property(m => m.CardholderName).HasMaxLength(256);
    }
}
