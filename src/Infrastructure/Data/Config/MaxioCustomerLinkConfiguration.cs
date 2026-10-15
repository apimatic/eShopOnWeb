using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class MaxioCustomerLinkConfiguration : IEntityTypeConfiguration<MaxioCustomerLink>
{
    public void Configure(EntityTypeBuilder<MaxioCustomerLink> builder)
    {
        builder.ToTable("MaxioCustomerLinks");
        builder.HasKey(l => l.UserId);
        builder.Property(l => l.UserId).HasMaxLength(450);
    }
}

public class MaxioSubscriptionLinkConfiguration : IEntityTypeConfiguration<MaxioSubscriptionLink>
{
    public void Configure(EntityTypeBuilder<MaxioSubscriptionLink> builder)
    {
        builder.ToTable("MaxioSubscriptionLinks");
        builder.HasKey(l => new { l.UserId, l.PlanHandle });
        builder.Property(l => l.UserId).HasMaxLength(450);
        builder.Property(l => l.PlanHandle).HasMaxLength(200);
        builder.Property(l => l.Reference).IsRequired().HasMaxLength(700);
    }
}
