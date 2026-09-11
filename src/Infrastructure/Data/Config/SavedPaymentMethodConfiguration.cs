using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.Property(m => m.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(m => m.VaultId).IsRequired().HasMaxLength(255);
        builder.Property(m => m.PayPalCustomerId).HasMaxLength(64);
        builder.Property(m => m.CardBrand).HasMaxLength(32);
        builder.Property(m => m.CardLast4).HasMaxLength(4);
        builder.Property(m => m.ExpiryMonthYear).HasMaxLength(7);
        builder.Property(m => m.CardholderName).HasMaxLength(300);

        builder.HasIndex(m => m.BuyerId);
    }
}
