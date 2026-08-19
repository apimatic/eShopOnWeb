using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SupplierSyncConfiguration : IEntityTypeConfiguration<SupplierSync>
{
    public void Configure(EntityTypeBuilder<SupplierSync> builder)
    {
        builder.ToTable("SupplierSyncs");

        builder.Property(s => s.SupplierId)
            .IsRequired(true);

        builder.Property(s => s.Status)
            .IsRequired(true)
            .HasConversion<int>();

        builder.Property(s => s.Error)
            .IsRequired(false)
            .HasMaxLength(2048);

        builder.HasIndex(s => s.SupplierId);
    }
}
