using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.DigitalFileAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class CatalogItemDigitalFileConfiguration : IEntityTypeConfiguration<CatalogItemDigitalFile>
{
    public void Configure(EntityTypeBuilder<CatalogItemDigitalFile> builder)
    {
        builder.ToTable("CatalogItemDigitalFiles");

        // The key is the catalog item id: one linked file per catalog item.
        builder.HasKey(f => f.Id);
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
