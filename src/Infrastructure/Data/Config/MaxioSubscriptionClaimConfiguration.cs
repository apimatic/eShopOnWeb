using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class MaxioSubscriptionClaimConfiguration : IEntityTypeConfiguration<MaxioSubscriptionClaim>
{
    public void Configure(EntityTypeBuilder<MaxioSubscriptionClaim> builder)
    {
        builder.ToTable("MaxioSubscriptionClaims");
        builder.HasKey(c => new { c.UserId, c.PlanHandle });
        builder.Property(c => c.UserId).HasMaxLength(450);
        builder.Property(c => c.PlanHandle).HasMaxLength(200);
        builder.Property(c => c.Reference).IsRequired().HasMaxLength(700);
    }
}
