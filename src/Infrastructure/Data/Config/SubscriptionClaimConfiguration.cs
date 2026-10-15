using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SubscriptionClaimConfiguration : IEntityTypeConfiguration<SubscriptionClaim>
{
    public void Configure(EntityTypeBuilder<SubscriptionClaim> builder)
    {
        builder.HasKey(c => new { c.UserKey, c.PlanHandle });
        builder.Property(c => c.UserKey).HasMaxLength(256);
        builder.Property(c => c.PlanHandle).HasMaxLength(256);
        builder.Property(c => c.Status).IsRequired().HasMaxLength(20);
        builder.Property(c => c.Reference).IsRequired().HasMaxLength(600);
    }
}

public class MaxioCustomerLinkConfiguration : IEntityTypeConfiguration<MaxioCustomerLink>
{
    public void Configure(EntityTypeBuilder<MaxioCustomerLink> builder)
    {
        builder.HasKey(c => c.UserKey);
        builder.Property(c => c.UserKey).HasMaxLength(256);
    }
}
