using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class ShopperContactNumberConfiguration : IEntityTypeConfiguration<ShopperContactNumber>
{
    public void Configure(EntityTypeBuilder<ShopperContactNumber> builder)
    {
        builder.Property(n => n.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(n => n.PhoneNumber)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(n => n.NationalFormat)
            .HasMaxLength(32);

        builder.Property(n => n.CountryCode)
            .HasMaxLength(2);

        builder.HasIndex(n => new { n.BuyerId, n.PhoneNumber }).IsUnique();
    }
}
