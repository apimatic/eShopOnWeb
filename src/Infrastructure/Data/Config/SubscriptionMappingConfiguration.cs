using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SubscriptionMappingConfiguration : IEntityTypeConfiguration<SubscriptionMapping>
{
    public void Configure(EntityTypeBuilder<SubscriptionMapping> builder)
    {
        builder.HasKey(mapping => mapping.Id);
        builder.Property(mapping => mapping.UserReference).HasMaxLength(256).IsRequired();
        builder.Property(mapping => mapping.SubscriptionReference).HasMaxLength(256).IsRequired();
        builder.Property(mapping => mapping.PlanHandle).HasMaxLength(128).IsRequired();
        builder.Property(mapping => mapping.State).HasMaxLength(64).IsRequired();
        builder.HasIndex(mapping => mapping.UserReference).IsUnique();
        builder.HasIndex(mapping => mapping.SubscriptionReference).IsUnique();
    }
}
