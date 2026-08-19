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
            .HasMaxLength(30);

        builder.Property(s => s.ItemsFound).IsRequired();
        builder.Property(s => s.ItemsImported).IsRequired();
        builder.Property(s => s.CreatedAt).IsRequired();

        builder.Property(s => s.ExternalJobId).HasMaxLength(200);
        builder.Property(s => s.ErrorMessage).HasMaxLength(2048);

        builder.HasOne<Supplier>()
            .WithMany()
            .HasForeignKey(s => s.SupplierId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
