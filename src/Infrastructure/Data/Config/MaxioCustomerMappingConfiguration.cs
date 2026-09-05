using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class MaxioCustomerMappingConfiguration : IEntityTypeConfiguration<MaxioCustomerMapping>
{
    public void Configure(EntityTypeBuilder<MaxioCustomerMapping> builder)
    {
        builder.ToTable("MaxioCustomerMappings");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.EshopUserId).HasMaxLength(128).IsRequired();
        builder.Property(m => m.MaxioCustomerId).IsRequired();
        builder.Property(m => m.CreatedAt).IsRequired();
        builder.Property(m => m.UpdatedAt).IsRequired();
        builder.HasIndex(m => m.EshopUserId).IsUnique();
    }
}
