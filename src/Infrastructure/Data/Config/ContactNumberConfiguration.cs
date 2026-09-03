using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class ContactNumberConfiguration : IEntityTypeConfiguration<ContactNumber>
{
    public void Configure(EntityTypeBuilder<ContactNumber> builder)
    {
        builder.Property(c => c.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(c => c.PhoneNumber)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(c => c.DeletedAt)
            .IsConcurrencyToken();

        builder.HasIndex(c => new { c.BuyerId, c.PhoneNumber })
            .IsUnique()
            .HasFilter("[DeletedAt] IS NULL");

        builder.HasIndex(c => new { c.BuyerId, c.DeletedAt });
    }
}
