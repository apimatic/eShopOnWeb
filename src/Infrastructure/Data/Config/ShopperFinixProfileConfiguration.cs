using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class ShopperFinixProfileConfiguration : IEntityTypeConfiguration<ShopperFinixProfile>
{
    public void Configure(EntityTypeBuilder<ShopperFinixProfile> builder)
    {
        builder.HasIndex(p => p.BuyerId).IsUnique();

        builder.Property(p => p.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(p => p.FinixIdentityId)
            .IsRequired()
            .HasMaxLength(128);
    }
}
