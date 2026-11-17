using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class ShopperFinixIdentityConfiguration : IEntityTypeConfiguration<ShopperFinixIdentity>
{
    public void Configure(EntityTypeBuilder<ShopperFinixIdentity> builder)
    {
        builder.HasKey(i => i.ShopperId);

        builder.Property(i => i.ShopperId)
            .HasMaxLength(256);

        builder.Property(i => i.Email)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(i => i.FinixIdentityId)
            .HasMaxLength(128);
    }
}
