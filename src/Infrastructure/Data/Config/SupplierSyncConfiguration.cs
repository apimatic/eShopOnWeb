using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SupplierSyncConfiguration : IEntityTypeConfiguration<SupplierSync>
{
    public void Configure(EntityTypeBuilder<SupplierSync> builder)
    {
        builder.ToTable("SupplierSyncs");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.SupplierId)
            .IsRequired();

        builder.Property(s => s.Status)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(s => s.ItemsFound).IsRequired();
        builder.Property(s => s.ItemsImported).IsRequired();
        builder.Property(s => s.ErrorMessage).HasMaxLength(2000);
        builder.Property(s => s.CreatedAt).IsRequired();

        builder.HasIndex(s => s.SupplierId);
    }
}
