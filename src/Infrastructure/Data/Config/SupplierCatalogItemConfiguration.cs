using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SupplierCatalogItemConfiguration : IEntityTypeConfiguration<SupplierCatalogItem>
{
    public void Configure(EntityTypeBuilder<SupplierCatalogItem> builder)
    {
        builder.ToTable("SupplierCatalogItems");

        builder.Property(link => link.SupplierId)
            .IsRequired(true);

        builder.Property(link => link.ExternalId)
            .IsRequired(true)
            .HasMaxLength(450);

        builder.Property(link => link.CatalogItemId)
            .IsRequired(true);

        // One catalog item per (supplier, external identifier): the guarantee that a re-sync
        // updates in place rather than duplicating.
        builder.HasIndex(link => new { link.SupplierId, link.ExternalId })
            .IsUnique();
    }
}
