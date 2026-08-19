using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class CatalogItemConfiguration : IEntityTypeConfiguration<CatalogItem>
{
    public void Configure(EntityTypeBuilder<CatalogItem> builder)
    {
        builder.ToTable("Catalog");

        builder.Property(ci => ci.Id)
            .UseHiLo("catalog_hilo")
            .IsRequired();

        builder.Property(ci => ci.Name)
            .IsRequired(true)
            .HasMaxLength(50);

        builder.Property(ci => ci.Price)
            .IsRequired(true)
            .HasColumnType("decimal(18,2)");

        builder.Property(ci => ci.PictureUri)
            .IsRequired(false);

        builder.Property(ci => ci.SupplierItemKey)
            .IsRequired(false)
            .HasMaxLength(512);

        builder.HasOne(ci => ci.CatalogBrand)
            .WithMany()
            .HasForeignKey(ci => ci.CatalogBrandId);

        builder.HasOne(ci => ci.CatalogType)
            .WithMany()
            .HasForeignKey(ci => ci.CatalogTypeId);

        builder.HasOne<Supplier>()
            .WithMany()
            .HasForeignKey(ci => ci.SupplierId)
            .OnDelete(DeleteBehavior.SetNull);

        // Imported items are matched by (supplier, supplier item key) so re-syncs never duplicate.
        builder.HasIndex(ci => new { ci.SupplierId, ci.SupplierItemKey });
    }
}
