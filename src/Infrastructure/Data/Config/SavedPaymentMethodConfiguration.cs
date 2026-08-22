using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.Property(m => m.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(m => m.PayPalPaymentTokenId)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(m => m.PayPalCustomerId)
            .HasMaxLength(64);

        builder.Property(m => m.Brand)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(m => m.LastDigits)
            .IsRequired()
            .HasMaxLength(4);

        builder.Property(m => m.Expiry)
            .IsRequired()
            .HasMaxLength(7);

        builder.Property(m => m.CardholderName)
            .HasMaxLength(100);

        builder.HasIndex(m => m.BuyerId);
        builder.HasIndex(m => m.PayPalPaymentTokenId).IsUnique();
    }
}
