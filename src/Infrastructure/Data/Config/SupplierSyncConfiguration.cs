using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SupplierSyncConfiguration : IEntityTypeConfiguration<SupplierSync>
{
    public void Configure(EntityTypeBuilder<SupplierSync> builder)
    {
        builder.ToTable("SupplierSyncs");

        builder.Property(s => s.Id).ValueGeneratedOnAdd();

        builder.Property(s => s.Status)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(s => s.ErrorMessage)
            .HasMaxLength(2000);

        builder.HasIndex(s => s.SupplierId);
    }
}
