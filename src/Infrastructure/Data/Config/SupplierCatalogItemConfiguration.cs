using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SupplierCatalogItemConfiguration : IEntityTypeConfiguration<SupplierCatalogItem>
{
    public void Configure(EntityTypeBuilder<SupplierCatalogItem> builder)
    {
        builder.ToTable("SupplierCatalogItems");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.SupplierId).IsRequired();

        // Capped so the (SupplierId, ExternalId) unique index stays within SQL Server's key-size
        // limit; long product URLs are hashed/truncated to this length by the sync service.
        builder.Property(m => m.ExternalId)
            .IsRequired()
            .HasMaxLength(400);

        builder.Property(m => m.CatalogItemId).IsRequired();
        builder.Property(m => m.LastSyncedDate).IsRequired();

        // One catalog item per (supplier, supplier's own product id): this is what keeps a re-sync
        // from importing the same product twice.
        builder.HasIndex(m => new { m.SupplierId, m.ExternalId }).IsUnique();
    }
}
