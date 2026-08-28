using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.Notifications;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class ContactNumberConfiguration : IEntityTypeConfiguration<ContactNumber>
{
    public void Configure(EntityTypeBuilder<ContactNumber> builder)
    {
        builder.Property(x => x.UserId).IsRequired().HasMaxLength(256);
        builder.Property(x => x.CanonicalNumber).IsRequired().HasMaxLength(32);
        builder.HasIndex(x => new { x.UserId, x.CanonicalNumber })
            .IsUnique()
            .HasFilter("[DeletedAt] IS NULL");
        builder.Ignore(x => x.IsActive);
    }
}
