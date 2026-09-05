using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SubscriptionPlanConfiguration : IEntityTypeConfiguration<SubscriptionPlan>
{
    public void Configure(EntityTypeBuilder<SubscriptionPlan> builder)
    {
        builder.HasKey(p => p.Id);

        builder.Property(p => p.MaxioProductId).IsRequired();
        builder.Property(p => p.Handle).IsRequired().HasMaxLength(100);
        builder.Property(p => p.Name).IsRequired().HasMaxLength(255);
        builder.Property(p => p.Description).HasMaxLength(1000);
        builder.Property(p => p.PriceInDollars).HasPrecision(18, 2);
        builder.Property(p => p.IntervalUnit).HasMaxLength(50);
        builder.Property(p => p.Interval).IsRequired();
        builder.Property(p => p.IsArchived).IsRequired().HasDefaultValue(false);
        builder.Property(p => p.CreatedAt).IsRequired();
        builder.Property(p => p.UpdatedAt).IsRequired();

        builder.HasIndex(p => p.Handle).IsUnique();
        builder.HasIndex(p => p.MaxioProductId).IsUnique();
    }
}
