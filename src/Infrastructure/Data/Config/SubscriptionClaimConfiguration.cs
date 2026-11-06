using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SubscriptionClaimConfiguration : IEntityTypeConfiguration<SubscriptionClaim>
{
    public void Configure(EntityTypeBuilder<SubscriptionClaim> builder)
    {
        // The composite primary key is what refuses a second claim for the same user and plan.
        builder.HasKey(c => new { c.UserName, c.PlanHandle });

        builder.Property(c => c.UserName).IsRequired().HasMaxLength(256);
        builder.Property(c => c.PlanHandle).IsRequired().HasMaxLength(256);
    }
}
