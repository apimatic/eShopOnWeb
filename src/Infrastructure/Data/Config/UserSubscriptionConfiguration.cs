using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class UserSubscriptionConfiguration : IEntityTypeConfiguration<UserSubscription>
{
    public void Configure(EntityTypeBuilder<UserSubscription> builder)
    {
        builder.Property(subscription => subscription.UserId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(subscription => subscription.CustomerReference)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(subscription => subscription.SubscriptionReference)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(subscription => subscription.PlanHandle)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(subscription => subscription.PlanName)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(subscription => subscription.Currency)
            .IsRequired()
            .HasMaxLength(3);

        builder.Property(subscription => subscription.State)
            .IsRequired()
            .HasMaxLength(32);

        builder.HasIndex(subscription => new { subscription.UserId, subscription.MaxioSubscriptionId })
            .IsUnique();

        builder.HasIndex(subscription => subscription.MaxioSubscriptionId);
    }
}
