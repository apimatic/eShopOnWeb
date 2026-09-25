using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedPaymentMethodAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.Property(p => p.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(p => p.VaultId).IsRequired().HasMaxLength(255);
        builder.Property(p => p.CardBrand).HasMaxLength(40);
        builder.Property(p => p.CardLast4).HasMaxLength(4);
        builder.Property(p => p.CardExpiry).HasMaxLength(7);

        builder.HasIndex(p => p.BuyerId);
    }
}
