using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class MaxioSubscriptionConfiguration : IEntityTypeConfiguration<MaxioSubscription>
{
    public void Configure(EntityTypeBuilder<MaxioSubscription> builder)
    {
        builder.ToTable("MaxioSubscriptions");

        builder.Property(s => s.UserId).HasMaxLength(450).IsRequired();
        builder.Property(s => s.PlanHandle).HasMaxLength(255).IsRequired();
        builder.Property(s => s.PlanName).HasMaxLength(255).IsRequired();
        builder.Property(s => s.IntervalUnit).HasMaxLength(50).IsRequired();
        builder.Property(s => s.State).HasMaxLength(50).IsRequired();
        builder.Property(s => s.Price).HasPrecision(18, 2);

        builder.HasIndex(s => s.UserId);
        builder.HasIndex(s => s.MaxioSubscriptionId).IsUnique();
    }
}
