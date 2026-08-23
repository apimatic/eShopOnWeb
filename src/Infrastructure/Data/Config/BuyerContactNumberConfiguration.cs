using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class BuyerContactNumberConfiguration : IEntityTypeConfiguration<BuyerContactNumber>
{
    public void Configure(EntityTypeBuilder<BuyerContactNumber> builder)
    {
        builder.ToTable("BuyerContactNumber");

        builder.Property(n => n.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(n => n.CanonicalNumber)
            .IsRequired()
            .HasMaxLength(32);

        builder.HasIndex(n => new { n.BuyerId, n.CanonicalNumber }).IsUnique();
    }
}
