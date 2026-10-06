using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.Maxio;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class MaxioSubscriptionMappingConfiguration : IEntityTypeConfiguration<MaxioSubscriptionMapping>
{
    public void Configure(EntityTypeBuilder<MaxioSubscriptionMapping> builder)
    {
        builder.ToTable("MaxioSubscriptionMappings");

        builder.Property(mapping => mapping.ApplicationUserId)
            .IsRequired()
            .HasMaxLength(450);

        builder.Property(mapping => mapping.MaxioSubscriptionId)
            .IsRequired();

        builder.Property(mapping => mapping.MaxioCustomerId)
            .IsRequired();

        builder.Property(mapping => mapping.ProductHandle)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(mapping => mapping.CreatedAtUtc)
            .IsRequired();

        builder.HasIndex(mapping => new { mapping.ApplicationUserId, mapping.ProductHandle })
            .IsUnique();
    }
}
