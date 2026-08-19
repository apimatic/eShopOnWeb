using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

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

        builder.HasOne(ci => ci.CatalogBrand)
            .WithMany()
            .HasForeignKey(ci => ci.CatalogBrandId);

        builder.HasOne(ci => ci.CatalogType)
            .WithMany()
            .HasForeignKey(ci => ci.CatalogTypeId);

        builder.Property(ci => ci.SupplierProductKey)
            .IsRequired(false)
            .HasMaxLength(2048);

        // Optional link back to the supplier a catalog item was imported from.
        builder.HasOne<Supplier>()
            .WithMany()
            .HasForeignKey(ci => ci.SupplierId)
            .OnDelete(DeleteBehavior.SetNull);

        // Speeds up (and expresses the intent of) the per-supplier product-key match used on re-sync.
        builder.HasIndex(ci => new { ci.SupplierId, ci.SupplierProductKey });
    }
}
