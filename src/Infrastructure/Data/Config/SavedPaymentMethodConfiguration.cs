using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.ToTable("SavedPaymentMethods");

        builder.HasKey(p => p.Id);
        builder.HasIndex(p => p.BuyerId);
        builder.HasIndex(p => p.PayPalPaymentTokenId).IsUnique();

        builder.Property(p => p.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(p => p.PayPalPaymentTokenId).IsRequired().HasMaxLength(255);
        builder.Property(p => p.PayPalCustomerId).HasMaxLength(64);
        builder.Property(p => p.LastDigits).IsRequired().HasMaxLength(4);
        builder.Property(p => p.Brand).IsRequired().HasMaxLength(64);
        builder.Property(p => p.Expiry).IsRequired().HasMaxLength(7);
        builder.Property(p => p.CardholderName).HasMaxLength(300);
    }
}
