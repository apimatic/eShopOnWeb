using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.ToTable("SavedPaymentMethods");
        builder.Property(m => m.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(m => m.PaymentTokenId).IsRequired().HasMaxLength(128);
        builder.Property(m => m.PayPalCustomerId).HasMaxLength(128);
        builder.Property(m => m.LastDigits).HasMaxLength(4);
        builder.Property(m => m.Brand).HasMaxLength(32);
        builder.Property(m => m.Expiry).HasMaxLength(7);
        builder.Property(m => m.CardholderName).HasMaxLength(300);
        builder.Property(m => m.CardType).HasMaxLength(32);
        builder.HasIndex(m => m.PaymentTokenId).IsUnique();
    }
}
