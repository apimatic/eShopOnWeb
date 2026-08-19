using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SupplierCatalogAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SupplierProductConfiguration : IEntityTypeConfiguration<SupplierProduct>
{
    public void Configure(EntityTypeBuilder<SupplierProduct> builder)
    {
        builder.ToTable("SupplierProducts");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.ExternalId)
            .IsRequired()
            .HasMaxLength(2048);

        // One catalog item per (supplier, external identifier) — the upsert key that keeps a
        // re-sync from duplicating a product already imported.
        builder.HasIndex(s => new { s.SupplierId, s.ExternalId }).IsUnique();
        builder.HasIndex(s => s.CatalogItemId);
    }
}
