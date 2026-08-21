using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.Property(pm => pm.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(pm => pm.VaultToken).IsRequired().HasMaxLength(128);
        builder.Property(pm => pm.Brand).HasMaxLength(40);
        builder.Property(pm => pm.Last4).HasMaxLength(4);
        builder.Property(pm => pm.Expiry).HasMaxLength(7);
        builder.Property(pm => pm.Alias).HasMaxLength(100);

        builder.HasIndex(pm => pm.BuyerId);
    }
}
