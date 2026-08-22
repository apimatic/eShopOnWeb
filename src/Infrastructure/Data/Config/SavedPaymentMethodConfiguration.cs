using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.ToTable("SavedPaymentMethods");

        builder.Property(m => m.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(m => m.PayPalPaymentTokenId)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(m => m.PayPalCustomerId).HasMaxLength(128);
        builder.Property(m => m.LastDigits).IsRequired().HasMaxLength(8);
        builder.Property(m => m.Brand).HasMaxLength(32);
        builder.Property(m => m.Expiry).HasMaxLength(16);
        builder.Property(m => m.CardholderName).HasMaxLength(128);

        builder.HasIndex(m => m.BuyerId);
        builder.HasIndex(m => m.PayPalPaymentTokenId).IsUnique();
    }
}
