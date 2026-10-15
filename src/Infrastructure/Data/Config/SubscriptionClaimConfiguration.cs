using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SubscriptionClaimConfiguration : IEntityTypeConfiguration<SubscriptionClaim>
{
    public void Configure(EntityTypeBuilder<SubscriptionClaim> builder)
    {
        builder.ToTable("SubscriptionClaims");
        builder.HasKey(c => new { c.BuyerId, c.PlanHandle });
        builder.Property(c => c.BuyerId).HasMaxLength(256);
        builder.Property(c => c.PlanHandle).HasMaxLength(256);
    }
}
