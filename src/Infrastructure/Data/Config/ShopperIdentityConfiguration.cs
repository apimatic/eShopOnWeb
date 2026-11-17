using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class ShopperIdentityConfiguration : IEntityTypeConfiguration<ShopperIdentity>
{
    public void Configure(EntityTypeBuilder<ShopperIdentity> builder)
    {
        builder.HasKey(i => i.BuyerId);
        builder.Property(i => i.BuyerId).HasMaxLength(256);
        builder.Property(i => i.FinixIdentityId).IsRequired().HasMaxLength(64);
        builder.HasIndex(i => i.FinixIdentityId).IsUnique();
    }
}
