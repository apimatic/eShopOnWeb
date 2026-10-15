using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class MaxioCustomerLinkConfiguration : IEntityTypeConfiguration<MaxioCustomerLink>
{
    public void Configure(EntityTypeBuilder<MaxioCustomerLink> builder)
    {
        builder.HasKey(l => l.UserId);
        builder.Property(l => l.UserId).HasMaxLength(450);
        builder.Property(l => l.Status).IsRequired().HasMaxLength(16);
        builder.Property(l => l.ClaimToken).IsConcurrencyToken();
    }
}

public class UserSubscriptionConfiguration : IEntityTypeConfiguration<UserSubscription>
{
    public void Configure(EntityTypeBuilder<UserSubscription> builder)
    {
        builder.HasKey(s => new { s.UserId, s.PlanHandle });
        builder.Property(s => s.UserId).HasMaxLength(450);
        builder.Property(s => s.PlanHandle).HasMaxLength(256);
        builder.Property(s => s.Status).IsRequired().HasMaxLength(16);
        builder.Property(s => s.ClaimToken).IsConcurrencyToken();
    }
}
