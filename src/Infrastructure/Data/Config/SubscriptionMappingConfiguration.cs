using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SubscriptionMappingConfiguration : IEntityTypeConfiguration<SubscriptionMapping>
{
    public void Configure(EntityTypeBuilder<SubscriptionMapping> builder)
    {
        builder.Property(mapping => mapping.UserId).IsRequired().HasMaxLength(450);
        builder.Property(mapping => mapping.ProductHandle).IsRequired().HasMaxLength(255);
        builder.Property(mapping => mapping.SubscriptionReference).IsRequired().HasMaxLength(450);
        builder.HasIndex(mapping => new { mapping.UserId, mapping.ProductHandle }).IsUnique();
        builder.HasIndex(mapping => mapping.SubscriptionReference).IsUnique();
    }
}
