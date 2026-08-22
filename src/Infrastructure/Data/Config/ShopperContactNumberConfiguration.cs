using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class ShopperContactNumberConfiguration : IEntityTypeConfiguration<ShopperContactNumber>
{
    public void Configure(EntityTypeBuilder<ShopperContactNumber> builder)
    {
        builder.Property(c => c.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(c => c.CanonicalNumber)
            .IsRequired()
            .HasMaxLength(32);

        builder.HasIndex(c => new { c.BuyerId, c.CanonicalNumber })
            .IsUnique();
    }
}
