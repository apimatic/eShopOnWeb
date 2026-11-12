using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class MaxioCustomerLinkConfiguration : IEntityTypeConfiguration<MaxioCustomerLink>
{
    public void Configure(EntityTypeBuilder<MaxioCustomerLink> builder)
    {
        builder.HasKey(l => l.UserId);
        builder.Property(l => l.UserId).HasMaxLength(256);
        builder.Property(l => l.Reference).IsRequired().HasMaxLength(300);
    }
}

public class MaxioSubscriptionLinkConfiguration : IEntityTypeConfiguration<MaxioSubscriptionLink>
{
    public void Configure(EntityTypeBuilder<MaxioSubscriptionLink> builder)
    {
        builder.HasKey(l => new { l.UserId, l.PlanHandle });
        builder.Property(l => l.UserId).HasMaxLength(256);
        builder.Property(l => l.PlanHandle).HasMaxLength(100);
        builder.Property(l => l.Reference).IsRequired().HasMaxLength(500);
    }
}
