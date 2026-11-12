using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class MaxioSubscriptionLinkConfiguration : IEntityTypeConfiguration<MaxioSubscriptionLink>
{
    public void Configure(EntityTypeBuilder<MaxioSubscriptionLink> builder)
    {
        builder.ToTable("MaxioSubscriptionLinks");
        builder.HasKey(l => new { l.UserId, l.PlanHandle });
        builder.Property(l => l.UserId).HasMaxLength(450).ValueGeneratedNever();
        builder.Property(l => l.PlanHandle).HasMaxLength(256).ValueGeneratedNever();
        builder.Property(l => l.SubscriptionReference).IsRequired().HasMaxLength(1024);
        builder.Property(l => l.ClaimedAtUtc).IsConcurrencyToken();
    }
}
