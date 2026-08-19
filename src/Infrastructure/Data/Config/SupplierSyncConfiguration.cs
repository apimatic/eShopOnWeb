using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SupplierSyncConfiguration : IEntityTypeConfiguration<SupplierSync>
{
    public void Configure(EntityTypeBuilder<SupplierSync> builder)
    {
        builder.ToTable("SupplierSyncs");

        builder.Property(s => s.Id)
            .UseHiLo("supplier_sync_hilo")
            .IsRequired();

        builder.Property(s => s.SupplierId)
            .IsRequired();

        builder.Property(s => s.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.Property(s => s.StatusDetail)
            .HasMaxLength(1000);

        builder.Property(s => s.CreatedAt)
            .IsRequired();

        builder.HasOne<Supplier>()
            .WithMany()
            .HasForeignKey(s => s.SupplierId);
    }
}
