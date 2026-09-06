using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class UserSubscriptionConfiguration : IEntityTypeConfiguration<UserSubscription>
{
    public void Configure(EntityTypeBuilder<UserSubscription> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.UserId).IsRequired().HasMaxLength(450);
        builder.Property(x => x.PlanHandle).IsRequired().HasMaxLength(250);
        builder.Property(x => x.PlanName).IsRequired().HasMaxLength(500);
        builder.Property(x => x.State).IsRequired().HasMaxLength(50);
        builder.Property(x => x.MaxioCustomerReference).IsRequired().HasMaxLength(250);
        builder.HasIndex(x => new { x.UserId, x.MaxioSubscriptionId }).IsUnique();
    }
}
