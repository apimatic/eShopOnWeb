using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SupplierCatalogItemConfiguration : IEntityTypeConfiguration<SupplierCatalogItem>
{
    public void Configure(EntityTypeBuilder<SupplierCatalogItem> builder)
    {
        builder.ToTable("SupplierCatalogItems");

        builder.Property(x => x.SupplierId).IsRequired();
        builder.Property(x => x.CatalogItemId).IsRequired();

        builder.Property(x => x.ExternalKey)
            .IsRequired()
            .HasMaxLength(1000);

        builder.Property(x => x.NameKey)
            .IsRequired()
            .HasMaxLength(500);

        // One catalog item per (supplier, external key): the primary guard against duplicate imports.
        builder.HasIndex(x => new { x.SupplierId, x.ExternalKey }).IsUnique();

        // Secondary lookup key on the product name, always present so re-syncs never duplicate.
        builder.HasIndex(x => new { x.SupplierId, x.NameKey });
    }
}
