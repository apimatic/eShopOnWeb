using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SubscriptionClaimConfiguration : IEntityTypeConfiguration<SubscriptionClaim>
{
    public void Configure(EntityTypeBuilder<SubscriptionClaim> builder)
    {
        builder.ToTable("SubscriptionClaims");

        // The primary key IS the duplicate guard: a second claim for the same user is refused by the store
        // (in-memory included).
        builder.HasKey(c => c.UserId);
        builder.Property(c => c.UserId).HasMaxLength(256).ValueGeneratedNever();
        builder.Property(c => c.PlanHandle).IsRequired().HasMaxLength(256);
        builder.Property(c => c.Version).IsConcurrencyToken();
    }
}
