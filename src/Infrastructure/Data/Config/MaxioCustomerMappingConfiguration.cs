using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class MaxioCustomerMappingConfiguration : IEntityTypeConfiguration<MaxioCustomerMapping>
{
    public void Configure(EntityTypeBuilder<MaxioCustomerMapping> builder)
    {
        builder.ToTable("MaxioCustomerMapping");

        builder.Property(m => m.UserName)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(m => m.MaxioCustomerReference)
            .IsRequired()
            .HasMaxLength(256);

        builder.HasIndex(m => m.UserName)
            .IsUnique();
    }
}
