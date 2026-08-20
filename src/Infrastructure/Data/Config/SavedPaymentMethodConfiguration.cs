using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedPaymentMethodAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.ToTable("SavedPaymentMethods");

        builder.Property(p => p.BuyerId)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(p => p.PayPalPaymentTokenId)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(p => p.Brand).HasMaxLength(32);
        builder.Property(p => p.LastDigits).HasMaxLength(4);
        builder.Property(p => p.Expiry).HasMaxLength(7);
        builder.Property(p => p.CardholderName).HasMaxLength(300);
        builder.Property(p => p.PayPalCustomerId).HasMaxLength(22);

        builder.HasIndex(p => p.BuyerId);
        builder.HasIndex(p => p.PayPalPaymentTokenId).IsUnique();
    }
}
