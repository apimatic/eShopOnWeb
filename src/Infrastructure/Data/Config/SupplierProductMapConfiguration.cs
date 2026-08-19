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
            .HasMaxLength(1000);

        builder.Property(m => m.CatalogItemId)
            .IsRequired();

        // One catalog item per (supplier, supplier's product identifier): this is the guard that
        // makes a re-sync update the same item instead of creating a duplicate.
        builder.HasIndex(m => new { m.SupplierId, m.ExternalId })
            .IsUnique();
    }
}
