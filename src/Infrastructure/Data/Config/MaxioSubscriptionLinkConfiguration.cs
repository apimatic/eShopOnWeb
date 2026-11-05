using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.MaxioAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class MaxioSubscriptionLinkConfiguration : IEntityTypeConfiguration<MaxioSubscriptionLink>
{
    public void Configure(EntityTypeBuilder<MaxioSubscriptionLink> builder)
    {
        builder.HasKey(l => new { l.UserKey, l.PlanHandle });
        builder.Property(l => l.UserKey).HasMaxLength(256);
        builder.Property(l => l.PlanHandle).HasMaxLength(256);
        builder.Property(l => l.Reference).IsRequired().HasMaxLength(512);
    }
}
