using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SupplierProductLinkConfiguration : IEntityTypeConfiguration<SupplierProductLink>
{
    public void Configure(EntityTypeBuilder<SupplierProductLink> builder)
    {
        builder.ToTable("SupplierProductLinks");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();

        builder.Property(l => l.SupplierId).IsRequired();

        builder.Property(l => l.ExternalId)
            .IsRequired()
            .HasMaxLength(2048);

        builder.Property(l => l.CatalogItemId).IsRequired();
        builder.Property(l => l.FirstImportedAt).IsRequired();
        builder.Property(l => l.LastSyncedAt).IsRequired();

        // A supplier's product (by its own identifier) maps to exactly one catalog item, which
        // is what keeps re-syncs from creating duplicates.
        builder.HasIndex(l => new { l.SupplierId, l.ExternalId }).IsUnique();
    }
}
