using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class CatalogStockEntryConfiguration : IEntityTypeConfiguration<CatalogStockEntry>
{
    public void Configure(EntityTypeBuilder<CatalogStockEntry> builder)
    {
        builder.ToTable("CatalogStock");

        builder.Property(s => s.Id)
            .UseHiLo("catalog_stock_hilo")
            .IsRequired();

        builder.Property(s => s.CatalogItemId)
            .IsRequired();

        builder.Property(s => s.Quantity)
            .IsRequired();

        // One stock level per catalog item.
        builder.HasIndex(s => s.CatalogItemId)
            .IsUnique();
    }
}
