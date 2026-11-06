using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class MaxioCustomerLinkConfiguration : IEntityTypeConfiguration<MaxioCustomerLink>
{
    public void Configure(EntityTypeBuilder<MaxioCustomerLink> builder)
    {
        builder.HasKey(l => l.UserName);
        builder.Property(l => l.UserName).HasMaxLength(256);
        builder.Property(l => l.Status).IsRequired().HasMaxLength(16);
    }
}

public class SubscriptionClaimConfiguration : IEntityTypeConfiguration<SubscriptionClaim>
{
    public void Configure(EntityTypeBuilder<SubscriptionClaim> builder)
    {
        builder.HasKey(c => new { c.UserName, c.PlanHandle });
        builder.Property(c => c.UserName).HasMaxLength(256);
        builder.Property(c => c.PlanHandle).HasMaxLength(128);
        builder.Property(c => c.Reference).IsRequired().HasMaxLength(512);
        builder.Property(c => c.Status).IsRequired().HasMaxLength(16);
    }
}
