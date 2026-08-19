using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SupplierCatalogItemConfiguration : IEntityTypeConfiguration<SupplierCatalogItem>
{
    public void Configure(EntityTypeBuilder<SupplierCatalogItem> builder)
    {
        builder.ToTable("SupplierCatalogItems");

        builder.Property(m => m.Id)
            .UseHiLo("supplier_catalog_item_hilo")
            .IsRequired();

        builder.Property(m => m.SupplierId)
            .IsRequired();

        builder.Property(m => m.ExternalId)
            .IsRequired()
            .HasMaxLength(2048);

        builder.Property(m => m.CatalogItemId)
            .IsRequired();

        // One catalog item per (supplier, external product key): the guarantee that a re-sync
        // updates the same item instead of duplicating it.
        builder.HasIndex(m => new { m.SupplierId, m.ExternalId }).IsUnique();
    }
}
