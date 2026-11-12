using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class MaxioCustomerLinkConfiguration : IEntityTypeConfiguration<MaxioCustomerLink>
{
    public void Configure(EntityTypeBuilder<MaxioCustomerLink> builder)
    {
        builder.ToTable("MaxioCustomerLinks");
        builder.HasKey(x => x.UserId);
        builder.Property(x => x.UserId).HasMaxLength(256).ValueGeneratedNever();
    }
}

public class MaxioSubscriptionLinkConfiguration : IEntityTypeConfiguration<MaxioSubscriptionLink>
{
    public void Configure(EntityTypeBuilder<MaxioSubscriptionLink> builder)
    {
        builder.ToTable("MaxioSubscriptionLinks");
        builder.HasKey(x => new { x.UserId, x.PlanHandle });
        builder.Property(x => x.UserId).HasMaxLength(256).ValueGeneratedNever();
        builder.Property(x => x.PlanHandle).HasMaxLength(256).ValueGeneratedNever();
        builder.Property(x => x.Reference).HasMaxLength(600).IsRequired();
    }
}
