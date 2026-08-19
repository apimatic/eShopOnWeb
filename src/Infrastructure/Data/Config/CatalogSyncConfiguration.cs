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
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(s => s.ItemsFound).IsRequired();
        builder.Property(s => s.ItemsImported).IsRequired();
        builder.Property(s => s.StartedAt).IsRequired();
        builder.Property(s => s.ErrorMessage).HasMaxLength(2000);

        builder.HasIndex(s => s.SupplierId);
    }
}
