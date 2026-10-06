using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.Maxio;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class MaxioCustomerMappingConfiguration : IEntityTypeConfiguration<MaxioCustomerMapping>
{
    public void Configure(EntityTypeBuilder<MaxioCustomerMapping> builder)
    {
        builder.ToTable("MaxioCustomerMappings");

        builder.Property(mapping => mapping.ApplicationUserId)
            .IsRequired()
            .HasMaxLength(450);

        builder.HasIndex(mapping => mapping.ApplicationUserId)
            .IsUnique();

        builder.Property(mapping => mapping.MaxioCustomerId)
            .IsRequired();

        builder.Property(mapping => mapping.MaxioCustomerReference)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(mapping => mapping.CreatedAtUtc)
            .IsRequired();
    }
}
