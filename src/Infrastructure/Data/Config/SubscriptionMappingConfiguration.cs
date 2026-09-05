using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public sealed class SubscriptionMappingConfiguration : IEntityTypeConfiguration<SubscriptionMapping>
{
    public void Configure(EntityTypeBuilder<SubscriptionMapping> builder)
    {
        builder.Property(mapping => mapping.UserId)
            .IsRequired()
            .HasMaxLength(450);

        builder.Property(mapping => mapping.PlanHandle)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(mapping => mapping.CreatedAt)
            .IsRequired()
            .HasColumnType("datetimeoffset");

        builder.Property(mapping => mapping.UpdatedAt)
            .IsRequired()
            .HasColumnType("datetimeoffset");

        builder.HasIndex(mapping => mapping.UserId)
            .IsUnique();

        builder.HasIndex(mapping => mapping.MaxioSubscriptionId)
            .IsUnique();
    }
}
