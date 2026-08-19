using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SupplierCatalogItemConfiguration : IEntityTypeConfiguration<SupplierCatalogItem>
{
    public void Configure(EntityTypeBuilder<SupplierCatalogItem> builder)
    {
        builder.ToTable("SupplierCatalogItems");

        builder.HasKey(link => link.Id);
        builder.Property(link => link.Id).ValueGeneratedOnAdd();

        builder.Property(link => link.SupplierId).IsRequired();

        builder.Property(link => link.ExternalId)
            .IsRequired()
            .HasMaxLength(512);

        builder.Property(link => link.CatalogItemId).IsRequired();
        builder.Property(link => link.FirstImportedAt).IsRequired();
        builder.Property(link => link.LastSyncedAt).IsRequired();

        // A supplier product (by its own identifier/URL) maps to exactly one catalog item.
        builder.HasIndex(link => new { link.SupplierId, link.ExternalId }).IsUnique();
    }
}
