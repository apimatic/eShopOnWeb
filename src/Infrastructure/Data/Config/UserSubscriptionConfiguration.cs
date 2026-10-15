using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class UserSubscriptionConfiguration : IEntityTypeConfiguration<UserSubscription>
{
    public void Configure(EntityTypeBuilder<UserSubscription> builder)
    {
        builder.HasKey(s => new { s.UserName, s.PlanHandle });
        builder.Property(s => s.UserName).HasMaxLength(256);
        builder.Property(s => s.PlanHandle).HasMaxLength(128);
        builder.Property(s => s.Reference).IsRequired().HasMaxLength(512);
        builder.Property(s => s.State).HasConversion<string>().HasMaxLength(16);
        builder.Property(s => s.ClaimedAt).IsConcurrencyToken();
    }
}
