using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class UserCustomerLinkConfiguration : IEntityTypeConfiguration<UserCustomerLink>
{
    public void Configure(EntityTypeBuilder<UserCustomerLink> builder)
    {
        builder.HasKey(l => l.UserId);
        builder.Property(l => l.UserId).HasMaxLength(450).ValueGeneratedNever();
    }
}

public class UserSubscriptionLinkConfiguration : IEntityTypeConfiguration<UserSubscriptionLink>
{
    public void Configure(EntityTypeBuilder<UserSubscriptionLink> builder)
    {
        builder.HasKey(l => l.UserId);
        builder.Property(l => l.UserId).HasMaxLength(450).ValueGeneratedNever();
        builder.Property(l => l.PlanHandle).IsRequired().HasMaxLength(256);
        builder.Property(l => l.Status).IsRequired().HasMaxLength(32);
    }
}
