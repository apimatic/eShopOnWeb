using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SubscriptionMappingConfiguration : IEntityTypeConfiguration<SubscriptionMapping>
{
    public void Configure(EntityTypeBuilder<SubscriptionMapping> builder)
    {
        builder.Property(x => x.UserId).IsRequired().HasMaxLength(256);
        builder.Property(x => x.ProductHandle).IsRequired().HasMaxLength(255);
        builder.Property(x => x.CustomerReference).IsRequired().HasMaxLength(300);
        builder.Property(x => x.SubscriptionReference).IsRequired().HasMaxLength(300);

        builder.HasIndex(x => new { x.UserId, x.ProductHandle }).IsUnique();
        builder.HasIndex(x => x.SubscriptionReference).IsUnique();
    }
}
