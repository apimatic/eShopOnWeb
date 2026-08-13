using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class ContactNumberConfiguration : IEntityTypeConfiguration<ContactNumber>
{
    public void Configure(EntityTypeBuilder<ContactNumber> builder)
    {
        builder.Property(c => c.OwnerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(c => c.Number)
            .IsRequired()
            .HasMaxLength(32);

        builder.HasIndex(c => new { c.OwnerId, c.Number }).IsUnique();
    }
}
