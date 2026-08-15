using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.Property(p => p.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(p => p.VaultId).IsRequired().HasMaxLength(128);
        builder.Property(p => p.CardBrand).HasMaxLength(40);
        builder.Property(p => p.Last4).HasMaxLength(4);
        builder.Property(p => p.ExpiryMonth).HasMaxLength(2);
        builder.Property(p => p.ExpiryYear).HasMaxLength(4);
        builder.Property(p => p.CardholderName).HasMaxLength(256);

        builder.HasIndex(p => p.BuyerId);
    }
}
