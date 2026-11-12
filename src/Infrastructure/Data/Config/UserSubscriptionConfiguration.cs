using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class UserSubscriptionConfiguration : IEntityTypeConfiguration<UserSubscription>
{
    public void Configure(EntityTypeBuilder<UserSubscription> builder)
    {
        builder.HasKey(s => new { s.UserId, s.PlanHandle });
        builder.Property(s => s.UserId).HasMaxLength(450);
        builder.Property(s => s.PlanHandle).HasMaxLength(256);
        builder.Property(s => s.SubscriptionReference).IsRequired().HasMaxLength(256);
        builder.Property(s => s.PlanName).HasMaxLength(512);
        builder.Property(s => s.IntervalUnit).HasMaxLength(32);
        builder.Property(s => s.State).HasMaxLength(64);
        builder.HasIndex(s => s.SubscriptionReference).IsUnique();
    }
}
