using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class ShopperProfileConfiguration : IEntityTypeConfiguration<ShopperProfile>
{
    public void Configure(EntityTypeBuilder<ShopperProfile> builder)
    {
        builder.Property(p => p.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(p => p.FinixIdentityId)
            .IsRequired()
            .HasMaxLength(64);

        builder.HasIndex(p => p.BuyerId).IsUnique();
    }
}
