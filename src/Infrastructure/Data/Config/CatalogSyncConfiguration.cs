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
            .HasConversion<int>();

        builder.Property(s => s.ExternalJobId)
            .HasMaxLength(200);

        builder.Property(s => s.ErrorMessage)
            .HasMaxLength(2000);

        builder.Property(s => s.CreatedDate)
            .IsRequired();

        builder.HasIndex(s => s.SupplierId);
    }
}
