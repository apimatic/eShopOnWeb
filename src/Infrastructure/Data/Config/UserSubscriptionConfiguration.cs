using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class UserSubscriptionConfiguration : IEntityTypeConfiguration<UserSubscription>
{
    public void Configure(EntityTypeBuilder<UserSubscription> builder)
    {
        builder.Property(subscription => subscription.UserId).IsRequired().HasMaxLength(450);
        builder.Property(subscription => subscription.Reference).IsRequired().HasMaxLength(100);
        builder.Property(subscription => subscription.ProductHandle).IsRequired().HasMaxLength(255);
        builder.Property(subscription => subscription.ProductName).IsRequired().HasMaxLength(255);
        builder.Property(subscription => subscription.IntervalUnit).IsRequired().HasMaxLength(16);
        builder.Property(subscription => subscription.State).IsRequired().HasMaxLength(32);

        builder.HasIndex(subscription => subscription.Reference).IsUnique();
        builder.HasIndex(subscription => subscription.MaxioSubscriptionId).IsUnique();
        builder.HasIndex(subscription => new { subscription.UserId, subscription.ProductHandle });
    }
}
