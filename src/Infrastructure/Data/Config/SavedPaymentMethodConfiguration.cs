using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.Property(p => p.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(p => p.VaultTokenId).IsRequired().HasMaxLength(200);
        builder.Property(p => p.Last4).HasMaxLength(4);
        builder.Property(p => p.CardBrand).HasMaxLength(50);
        builder.Property(p => p.PayPalCustomerId).HasMaxLength(200);
    }
}
