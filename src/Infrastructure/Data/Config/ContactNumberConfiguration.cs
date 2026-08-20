using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class ContactNumberConfiguration : IEntityTypeConfiguration<ContactNumber>
{
    public void Configure(EntityTypeBuilder<ContactNumber> builder)
    {
        builder.ToTable("ContactNumbers");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(c => c.PhoneNumber)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(c => c.NationalFormat)
            .HasMaxLength(64);

        builder.Property(c => c.CountryCode)
            .HasMaxLength(8);

        builder.Property(c => c.LineType)
            .HasMaxLength(32);

        builder.HasIndex(c => new { c.BuyerId, c.PhoneNumber })
            .IsUnique();
    }
}
