using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.ToTable("SavedPaymentMethods");

        builder.Property(x => x.BuyerId).HasMaxLength(256).IsRequired();
        builder.Property(x => x.PayPalVaultId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.PayPalCustomerId).HasMaxLength(64);
        builder.Property(x => x.CardBrand).HasMaxLength(32);
        builder.Property(x => x.Last4).HasMaxLength(4);
        builder.Property(x => x.Expiry).HasMaxLength(7);
        builder.Property(x => x.CardholderName).HasMaxLength(128);

        builder.HasIndex(x => x.BuyerId);
    }
}
