using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class ShopperProfileConfiguration : IEntityTypeConfiguration<ShopperProfile>
{
    public void Configure(EntityTypeBuilder<ShopperProfile> builder)
    {
        builder.HasKey(p => p.BuyerId);
        builder.Property(p => p.BuyerId).HasMaxLength(256).IsRequired();
        builder.Property(p => p.FinixIdentityId).HasMaxLength(64).IsRequired();
        builder.Property(p => p.Email).HasMaxLength(256);
    }
}
