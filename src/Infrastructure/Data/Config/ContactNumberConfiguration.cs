using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class ContactNumberConfiguration : IEntityTypeConfiguration<ContactNumber>
{
    public void Configure(EntityTypeBuilder<ContactNumber> builder)
    {
        builder.ToTable("ContactNumbers");

        builder.HasKey(number => number.Id);

        builder.Property(number => number.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(number => number.PhoneNumber)
            .IsRequired()
            .HasMaxLength(32);

        builder.HasIndex(number => new { number.BuyerId, number.PhoneNumber })
            .IsUnique();
    }
}
