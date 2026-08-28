using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class ContactNumberConfiguration : IEntityTypeConfiguration<ContactNumber>
{
    public void Configure(EntityTypeBuilder<ContactNumber> builder)
    {
        builder.Property(x => x.OwnerId).IsRequired().HasMaxLength(256);
        builder.Property(x => x.PhoneNumber).IsRequired().HasMaxLength(32);
        builder.Ignore(x => x.IsActive);

        builder.HasIndex(x => new { x.OwnerId, x.PhoneNumber })
            .IsUnique()
            .HasFilter("[DeletedAt] IS NULL");
        builder.HasIndex(x => new { x.OwnerId, x.DeletedAt });
    }
}
