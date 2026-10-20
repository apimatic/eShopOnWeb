using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SubscriptionClaimConfiguration : IEntityTypeConfiguration<SubscriptionClaim>
{
    public void Configure(EntityTypeBuilder<SubscriptionClaim> builder)
    {
        builder.HasKey(c => c.ClaimKey);
        builder.Property(c => c.ClaimKey).IsRequired().HasMaxLength(512).ValueGeneratedNever();
    }
}

public class CustomerLinkConfiguration : IEntityTypeConfiguration<CustomerLink>
{
    public void Configure(EntityTypeBuilder<CustomerLink> builder)
    {
        builder.HasKey(c => c.UserKey);
        builder.Property(c => c.UserKey).IsRequired().HasMaxLength(256).ValueGeneratedNever();
    }
}
