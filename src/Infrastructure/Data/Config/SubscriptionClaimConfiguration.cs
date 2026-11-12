using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SubscriptionClaimConfiguration : IEntityTypeConfiguration<SubscriptionClaim>
{
    public void Configure(EntityTypeBuilder<SubscriptionClaim> builder)
    {
        builder.HasKey(c => c.UserId);
        builder.Property(c => c.UserId).HasMaxLength(256).ValueGeneratedNever();
        builder.Property(c => c.PlanHandle).IsRequired().HasMaxLength(256);
        builder.Property(c => c.SubscriptionReference).IsRequired().HasMaxLength(300);
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(32);
    }
}
