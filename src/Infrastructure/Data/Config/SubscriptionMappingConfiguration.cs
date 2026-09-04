using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SubscriptionMappingConfiguration : IEntityTypeConfiguration<SubscriptionMapping>
{
    public void Configure(EntityTypeBuilder<SubscriptionMapping> builder)
    {
        builder.HasKey(mapping => mapping.Id);
        builder.Property(mapping => mapping.UserId).HasMaxLength(450).IsRequired();
        builder.Property(mapping => mapping.CustomerReference).HasMaxLength(255).IsRequired();
        builder.Property(mapping => mapping.SubscriptionReference).HasMaxLength(255).IsRequired();
        builder.Property(mapping => mapping.PlanHandle).HasMaxLength(255).IsRequired();
        builder.HasIndex(mapping => new { mapping.UserId, mapping.PlanHandle }).IsUnique();
        builder.HasIndex(mapping => mapping.CustomerReference).IsUnique();
        builder.HasIndex(mapping => mapping.SubscriptionReference).IsUnique();
    }
}
