using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public sealed class ContactNumberConfiguration : IEntityTypeConfiguration<ContactNumber>
{
    public void Configure(EntityTypeBuilder<ContactNumber> builder)
    {
        builder.ToTable("ContactNumbers");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ShopperId).HasMaxLength(256).IsRequired();
        builder.Property(x => x.CanonicalNumber).HasMaxLength(32).IsRequired();
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => new { x.ShopperId, x.CanonicalNumber })
            .IsUnique()
            .HasFilter("[DeletedAt] IS NULL");
        builder.HasIndex(x => new { x.ShopperId, x.DeletedAt });
    }
}
