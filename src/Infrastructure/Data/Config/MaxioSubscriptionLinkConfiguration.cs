using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class MaxioSubscriptionLinkConfiguration : IEntityTypeConfiguration<MaxioSubscriptionLink>
{
    public void Configure(EntityTypeBuilder<MaxioSubscriptionLink> builder)
    {
        // The composite primary key is the claim: the store refuses a second subscription of a user to the same plan.
        builder.HasKey(l => new { l.UserName, l.PlanHandle });
        builder.Property(l => l.UserName).HasMaxLength(256);
        builder.Property(l => l.PlanHandle).HasMaxLength(256);
        builder.Property(l => l.Reference).IsRequired().HasMaxLength(256);
    }
}
