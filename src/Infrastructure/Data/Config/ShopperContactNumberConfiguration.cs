using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class ShopperContactNumberConfiguration : IEntityTypeConfiguration<ShopperContactNumber>
{
    public void Configure(EntityTypeBuilder<ShopperContactNumber> builder)
    {
        builder.ToTable("ShopperContactNumbers");

        builder.Property(n => n.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(n => n.CanonicalNumber)
            .IsRequired()
            .HasMaxLength(32);

        builder.HasIndex(n => n.CanonicalNumber)
            .IsUnique();

        builder.HasIndex(n => n.BuyerId);
    }
}
