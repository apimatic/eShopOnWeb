using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class CatalogItemDigitalFileConfiguration : IEntityTypeConfiguration<CatalogItemDigitalFile>
{
    public void Configure(EntityTypeBuilder<CatalogItemDigitalFile> builder)
    {
        builder.ToTable("CatalogItemDigitalFiles");

        // The key is the catalog item id itself: one linked file per item, and a second
        // concurrent insert for the same item is refused by the primary key.
        builder.Property(f => f.Id)
            .ValueGeneratedNever();

        builder.HasOne<CatalogItem>()
            .WithOne()
            .HasForeignKey<CatalogItemDigitalFile>(f => f.Id)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Ignore(f => f.CatalogItemId);

        builder.Property(f => f.FileId)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(f => f.FileName)
            .IsRequired()
            .HasMaxLength(255);
    }
}
