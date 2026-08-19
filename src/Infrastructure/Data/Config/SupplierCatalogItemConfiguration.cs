using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SupplierCatalogItemConfiguration : IEntityTypeConfiguration<SupplierCatalogItem>
{
    public void Configure(EntityTypeBuilder<SupplierCatalogItem> builder)
    {
        builder.ToTable("SupplierCatalogItems");

        builder.Property(s => s.Id).ValueGeneratedOnAdd();

        builder.Property(s => s.ExternalId)
            .IsRequired()
            .HasMaxLength(450);

        // One catalog item per (supplier, supplier's-own-product-id): the basis for idempotent re-syncs.
        builder.HasIndex(s => new { s.SupplierId, s.ExternalId }).IsUnique();
    }
}
