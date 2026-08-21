using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class BuyerContactNumberConfiguration : IEntityTypeConfiguration<BuyerContactNumber>
{
    public void Configure(EntityTypeBuilder<BuyerContactNumber> builder)
    {
        builder.ToTable("BuyerContactNumbers");

        builder.Property(n => n.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(n => n.PhoneNumber)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(n => n.NationalFormat)
            .HasMaxLength(64);

        builder.HasIndex(n => new { n.BuyerId, n.PhoneNumber })
            .IsUnique();
    }
}
