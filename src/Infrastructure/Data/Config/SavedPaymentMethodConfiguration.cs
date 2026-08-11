using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.ToTable("SavedPaymentMethods");

        builder.Property(m => m.BuyerId).HasMaxLength(256).IsRequired();
        builder.Property(m => m.VaultId).HasMaxLength(128).IsRequired();
        builder.Property(m => m.PayPalCustomerId).HasMaxLength(128);
        builder.Property(m => m.Brand).HasMaxLength(40).IsRequired();
        builder.Property(m => m.LastDigits).HasMaxLength(8).IsRequired();
        builder.Property(m => m.Expiry).HasMaxLength(7).IsRequired();
        builder.Property(m => m.CardholderName).HasMaxLength(256);

        builder.HasIndex(m => m.BuyerId);
    }
}
