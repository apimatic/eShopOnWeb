using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class MaxioCustomerMappingConfiguration : IEntityTypeConfiguration<MaxioCustomerMapping>
{
    public void Configure(EntityTypeBuilder<MaxioCustomerMapping> builder)
    {
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Id)
            .UseHiLo("maxio_customer_mapping_hilo")
            .IsRequired();

        builder.Property(m => m.ApplicationUserId)
            .IsRequired()
            .HasMaxLength(450);

        builder.Property(m => m.MaxioCustomerId)
            .IsRequired();

        builder.Property(m => m.CreatedAt)
            .IsRequired();

        builder.Property(m => m.UpdatedAt)
            .IsRequired();

        builder.HasIndex(m => m.ApplicationUserId)
            .IsUnique();

        builder.HasIndex(m => m.MaxioCustomerId)
            .IsUnique();
    }
}
