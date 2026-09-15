using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.Property(p => p.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(p => p.PayPalPaymentTokenId)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(p => p.PayPalCustomerId)
            .HasMaxLength(64);

        builder.Property(p => p.Last4)
            .IsRequired()
            .HasMaxLength(4);

        builder.Property(p => p.Brand)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(p => p.Expiry)
            .IsRequired()
            .HasMaxLength(7);

        builder.Property(p => p.CardholderName)
            .HasMaxLength(300);

        builder.HasIndex(p => p.BuyerId);
        builder.HasIndex(p => p.PayPalPaymentTokenId)
            .IsUnique();
    }
}
