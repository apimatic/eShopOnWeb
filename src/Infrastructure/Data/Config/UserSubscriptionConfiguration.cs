using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class UserSubscriptionConfiguration : IEntityTypeConfiguration<UserSubscription>
{
    public void Configure(EntityTypeBuilder<UserSubscription> builder)
    {
        builder.HasKey(s => new { s.BuyerId, s.PlanHandle });
        builder.Property(s => s.BuyerId).HasMaxLength(256);
        builder.Property(s => s.PlanHandle).HasMaxLength(200);
        builder.Property(s => s.Reference).IsRequired().HasMaxLength(100);
        builder.Property(s => s.Status).IsRequired().HasMaxLength(20);
    }
}
