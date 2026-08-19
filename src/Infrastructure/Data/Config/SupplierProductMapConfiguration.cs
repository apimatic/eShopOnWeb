using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SupplierProductMapConfiguration : IEntityTypeConfiguration<SupplierProductMap>
{
    public void Configure(EntityTypeBuilder<SupplierProductMap> builder)
    {
        builder.ToTable("SupplierProductMaps");

        builder.Property(m => m.SupplierId)
            .IsRequired();

        builder.Property(m => m.ExternalId)
            .IsRequired()
            .HasMaxLength(512);

        builder.Property(m => m.CatalogItemId)
            .IsRequired();

        // The supplier's own identifier for a product is unique per supplier: this is the
        // anchor that keeps a re-sync from importing the same product twice.
        builder.HasIndex(m => new { m.SupplierId, m.ExternalId })
            .IsUnique();
    }
}
