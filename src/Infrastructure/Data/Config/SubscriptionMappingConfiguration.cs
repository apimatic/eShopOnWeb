using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public sealed class MaxioCustomerMappingConfiguration : IEntityTypeConfiguration<MaxioCustomerMapping>
{
    public void Configure(EntityTypeBuilder<MaxioCustomerMapping> builder)
    {
        builder.HasKey(mapping => mapping.Id);
        builder.Property(mapping => mapping.ApplicationUserId).IsRequired().HasMaxLength(450);
        builder.Property(mapping => mapping.MaxioReference).IsRequired().HasMaxLength(255);
        builder.HasIndex(mapping => mapping.ApplicationUserId).IsUnique();
        builder.HasIndex(mapping => mapping.MaxioReference).IsUnique();
    }
}

public sealed class MaxioSubscriptionMappingConfiguration : IEntityTypeConfiguration<MaxioSubscriptionMapping>
{
    public void Configure(EntityTypeBuilder<MaxioSubscriptionMapping> builder)
    {
        builder.HasKey(mapping => mapping.Id);
        builder.Property(mapping => mapping.ApplicationUserId).IsRequired().HasMaxLength(450);
        builder.Property(mapping => mapping.PlanHandle).IsRequired().HasMaxLength(255);
        builder.Property(mapping => mapping.MaxioReference).IsRequired().HasMaxLength(255);
        builder.HasIndex(mapping => new { mapping.ApplicationUserId, mapping.PlanHandle }).IsUnique();
        builder.HasIndex(mapping => mapping.MaxioReference).IsUnique();
    }
}
