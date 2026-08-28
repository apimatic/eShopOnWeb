using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.Notifications;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class ContactNumberConfiguration : IEntityTypeConfiguration<ContactNumber>
{
    public void Configure(EntityTypeBuilder<ContactNumber> builder)
    {
        builder.Property(x => x.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(x => x.CanonicalNumber).IsRequired().HasMaxLength(32);
        builder.Ignore(x => x.IsActive);
        builder.HasIndex(x => new { x.BuyerId, x.CanonicalNumber })
            .IsUnique()
            .HasFilter("[DeletedAt] IS NULL");
    }
}
