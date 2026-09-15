using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.Property(x => x.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(x => x.PayPalVaultId).IsRequired().HasMaxLength(64);
        builder.Property(x => x.PayPalCustomerId).IsRequired().HasMaxLength(64);
        builder.Property(x => x.CardBrand).HasMaxLength(32);
        builder.Property(x => x.CardLast4).HasMaxLength(4);
        builder.Property(x => x.CardExpiry).HasMaxLength(7);
        builder.Property(x => x.CardholderName).HasMaxLength(128);

        builder.HasIndex(x => x.BuyerId);
    }
}
