using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class CatalogSyncConfiguration : IEntityTypeConfiguration<CatalogSync>
{
    public void Configure(EntityTypeBuilder<CatalogSync> builder)
    {
        builder.ToTable("CatalogSyncs");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(s => s.ItemsFound).IsRequired();
        builder.Property(s => s.ItemsImported).IsRequired();
        builder.Property(s => s.StartedAt).IsRequired();
        builder.Property(s => s.Error).HasMaxLength(2048);

        builder.HasOne<Supplier>()
            .WithMany()
            .HasForeignKey(s => s.SupplierId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
