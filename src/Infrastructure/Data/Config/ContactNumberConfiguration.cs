using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class ContactNumberConfiguration : IEntityTypeConfiguration<ContactNumber>
{
    public void Configure(EntityTypeBuilder<ContactNumber> builder)
    {
        builder.Property(x => x.BuyerId).HasMaxLength(256).IsRequired();
        builder.Property(x => x.E164Number).HasMaxLength(32).IsRequired();
        builder.HasIndex(x => new { x.BuyerId, x.E164Number })
            .IsUnique()
            .HasFilter("[DeletedAt] IS NULL");
        builder.Ignore(x => x.IsActive);
    }
}
