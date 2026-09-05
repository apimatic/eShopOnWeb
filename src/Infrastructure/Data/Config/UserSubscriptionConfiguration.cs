using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class UserSubscriptionConfiguration : IEntityTypeConfiguration<UserSubscription>
{
    public void Configure(EntityTypeBuilder<UserSubscription> builder)
    {
        builder.HasKey(s => s.Id);

        builder.Property(s => s.UserId).IsRequired().HasMaxLength(255);
        builder.Property(s => s.MaxioCustomerId).IsRequired();
        builder.Property(s => s.MaxioSubscriptionId).IsRequired();
        builder.Property(s => s.SubscriptionPlanId).IsRequired();
        builder.Property(s => s.State).IsRequired().HasMaxLength(50);
        builder.Property(s => s.CurrentPriceInDollars).HasPrecision(18, 2);
        builder.Property(s => s.NextAssessmentAt);
        builder.Property(s => s.CreatedAt).IsRequired();
        builder.Property(s => s.UpdatedAt).IsRequired();

        builder.HasIndex(s => s.UserId);
        builder.HasIndex(s => s.MaxioSubscriptionId).IsUnique();
        builder.HasIndex(s => new { s.UserId, s.MaxioSubscriptionId });
    }
}
