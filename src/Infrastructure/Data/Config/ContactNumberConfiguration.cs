using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public sealed class ContactNumberConfiguration : IEntityTypeConfiguration<ContactNumber>
{
    public void Configure(EntityTypeBuilder<ContactNumber> builder)
    {
        builder.Property(x => x.ShopperId).IsRequired().HasMaxLength(256);
        builder.Property(x => x.CanonicalNumber).IsRequired().HasMaxLength(32);
        builder.HasIndex(x => new { x.ShopperId, x.CanonicalNumber }).IsUnique();
    }
}
