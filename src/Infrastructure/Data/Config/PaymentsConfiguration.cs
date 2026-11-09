using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.Payments;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class ShopperProfileConfiguration : IEntityTypeConfiguration<ShopperProfile>
{
    public void Configure(EntityTypeBuilder<ShopperProfile> builder)
    {
        builder.Property(p => p.Email).IsRequired().HasMaxLength(256);
        builder.Property(p => p.StaxCustomerId).IsRequired().HasMaxLength(64);
        builder.HasIndex(p => p.Email).IsUnique();
    }
}

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.Property(p => p.ShopperEmail).IsRequired().HasMaxLength(256);
        builder.Property(p => p.StaxPaymentMethodId).IsRequired().HasMaxLength(64);
        builder.HasIndex(p => p.ShopperEmail);
        builder.HasIndex(p => p.StaxPaymentMethodId).IsUnique();
    }
}
