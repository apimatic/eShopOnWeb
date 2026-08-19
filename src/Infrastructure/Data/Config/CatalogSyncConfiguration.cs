using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class CatalogSyncConfiguration : IEntityTypeConfiguration<CatalogSync>
{
    public void Configure(EntityTypeBuilder<CatalogSync> builder)
    {
        builder.ToTable("CatalogSyncs");

        builder.Property(s => s.SupplierId)
            .IsRequired();

        builder.Property(s => s.Status)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(s => s.ItemsFound).IsRequired();
        builder.Property(s => s.ItemsImported).IsRequired();
        builder.Property(s => s.RequestedAt).IsRequired();
        builder.Property(s => s.ErrorMessage).IsRequired(false);

        builder.HasIndex(s => s.SupplierId);
    }
}
