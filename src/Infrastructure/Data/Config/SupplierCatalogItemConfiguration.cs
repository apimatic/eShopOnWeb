using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;
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
            .HasMaxLength(400);

        builder.Property(m => m.CatalogItemId)
            .IsRequired();

        // A supplier's product identifier maps to exactly one catalog item — this is what keeps
        // re-syncs idempotent.
        builder.HasIndex(m => new { m.SupplierId, m.ExternalId })
            .IsUnique();

        builder.HasOne<Supplier>()
            .WithMany()
            .HasForeignKey(m => m.SupplierId);

        builder.HasOne<CatalogItem>()
            .WithMany()
            .HasForeignKey(m => m.CatalogItemId);
    }
}
