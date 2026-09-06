using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class UserSubscriptionMappingEntityTypeConfiguration : IEntityTypeConfiguration<UserSubscriptionMapping>
{
    public void Configure(EntityTypeBuilder<UserSubscriptionMapping> builder)
    {
        builder.HasKey(m => m.Id);

        builder.Property(m => m.UserId)
            .IsRequired();

        builder.HasIndex(m => m.UserId)
            .IsUnique();
    }
}
