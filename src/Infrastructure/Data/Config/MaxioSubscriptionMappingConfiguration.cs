using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class MaxioSubscriptionMappingConfiguration : IEntityTypeConfiguration<MaxioSubscriptionMapping>
{
    public void Configure(EntityTypeBuilder<MaxioSubscriptionMapping> builder)
    {
        builder.HasKey(mapping => mapping.Id);
        builder.Property(mapping => mapping.UserId).HasMaxLength(450).IsRequired();
        builder.Property(mapping => mapping.PlanHandle).HasMaxLength(255).IsRequired();
        builder.Property(mapping => mapping.SubscriptionReference).HasMaxLength(255).IsRequired();
        builder.HasIndex(mapping => new { mapping.UserId, mapping.SubscriptionReference }).IsUnique();
        builder.HasIndex(mapping => new { mapping.UserId, mapping.MaxioSubscriptionId }).IsUnique();
    }
}
