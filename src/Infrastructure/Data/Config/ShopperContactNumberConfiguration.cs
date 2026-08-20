using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class ShopperContactNumberConfiguration : IEntityTypeConfiguration<ShopperContactNumber>
{
    public void Configure(EntityTypeBuilder<ShopperContactNumber> builder)
    {
        builder.ToTable("ShopperContactNumbers");

        builder.Property(number => number.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(number => number.PhoneNumber)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(number => number.NationalFormat)
            .HasMaxLength(64);

        builder.HasIndex(number => new { number.BuyerId, number.PhoneNumber })
            .IsUnique();
    }
}
