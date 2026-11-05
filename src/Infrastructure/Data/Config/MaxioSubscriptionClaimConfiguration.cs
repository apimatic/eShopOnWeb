using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class MaxioSubscriptionClaimConfiguration : IEntityTypeConfiguration<MaxioSubscriptionClaim>
{
    public void Configure(EntityTypeBuilder<MaxioSubscriptionClaim> builder)
    {
        builder.HasKey(c => new { c.UserKey, c.PlanHandle });
        builder.Property(c => c.UserKey).HasMaxLength(256);
        builder.Property(c => c.PlanHandle).HasMaxLength(256);
    }
}
