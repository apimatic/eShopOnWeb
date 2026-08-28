using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class ContactNumberConfiguration : IEntityTypeConfiguration<ContactNumber>
{
    public void Configure(EntityTypeBuilder<ContactNumber> builder)
    {
        builder.Property(x => x.OwnerId).HasMaxLength(256).IsRequired();
        builder.Property(x => x.CanonicalNumber).HasMaxLength(32).IsRequired();
        builder.Ignore(x => x.IsActive);

        builder.HasIndex(x => new { x.OwnerId, x.CanonicalNumber })
            .IsUnique()
            .HasFilter("[RemovedAt] IS NULL");
    }
}
