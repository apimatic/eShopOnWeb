using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SubscriptionMappingConfiguration : IEntityTypeConfiguration<SubscriptionMapping>
{
    public void Configure(EntityTypeBuilder<SubscriptionMapping> builder)
    {
        builder.HasKey(mapping => mapping.Id);

        builder.Property(mapping => mapping.UserId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(mapping => mapping.SubscriptionReference)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(mapping => mapping.ProductHandle)
            .IsRequired()
            .HasMaxLength(100);

        builder.HasIndex(mapping => mapping.UserId)
            .IsUnique();

        builder.HasIndex(mapping => mapping.SubscriptionReference)
            .IsUnique();
    }
}
